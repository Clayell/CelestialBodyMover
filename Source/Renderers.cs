// Adapted from TWP2 with permission (https://github.com/Nazfib/TransferWindowPlanner2/tree/main/TransferWindowPlanner2/UI/Rendering), thanks Nazfib!
// I think the original code was taken from KSP's AngleRenderEject, which seems to be a class that is hardly used

using LibNoise.Models;
using System;
using System.Reflection.Emit;
using UnityEngine;

namespace CelestialBodyMover
{
    internal static class RenderUtils
    {
        internal static LineRenderer InitLine(GameObject objToAttach, Color lineColor, int initialWidth, Material linesMaterial)
        {
            objToAttach.layer = 9;
            LineRenderer lineReturn = objToAttach.AddComponent<LineRenderer>();

            lineReturn.material = linesMaterial;
            lineReturn.startColor = lineColor;
            lineReturn.endColor = lineColor;
            lineReturn.transform.parent = null;
            lineReturn.useWorldSpace = true;
            lineReturn.startWidth = initialWidth;
            lineReturn.endWidth = initialWidth;
            lineReturn.enabled = false;

            return lineReturn;
        }

        internal static void DrawLine(LineRenderer line, Vector3d center, Vector3d start, Vector3d end)
        {
            if (line == null) return;

            Vector3 startPos = ScaledSpace.LocalToScaledSpace(center + start);
            Vector3 endPos = ScaledSpace.LocalToScaledSpace(center + end);
            Vector3 camPos = PlanetariumCamera.Camera.transform.position;

            Vector3 dist = endPos - startPos;
            Vector3 dir = dist.normalized;
            Vector3 viewDir = (endPos - camPos).normalized;
            float arrowSize = 0.05f * dist.magnitude;

            Vector3 right = Vector3.Cross(dir, viewDir).normalized;
            Vector3 dirArrow = dir * arrowSize;
            Vector3 rightArrow = right * arrowSize * 0.5f;

            Vector3 arrowLeft = endPos - dirArrow + rightArrow;
            Vector3 arrowRight = endPos - dirArrow - rightArrow;

            line.positionCount = 5;
            line.SetPosition(0, startPos);
            line.SetPosition(1, endPos);
            line.SetPosition(2, arrowLeft);
            line.SetPosition(3, arrowRight);
            line.SetPosition(4, endPos);

            line.startWidth = line.endWidth = 0.002f * Vector3.Distance(camPos, startPos);
            line.enabled = true;
        }
    }

    public class MapLineRenderer : MonoBehaviour
    {
        internal bool IsDrawing => _currentDrawingState != DrawingState.Hidden;

        internal bool IsHidden => _currentDrawingState == DrawingState.Hidden;

        internal bool IsHiding => _currentDrawingState == DrawingState.Hiding || _currentDrawingState == DrawingState.Hidden;

        float lineLength 
        { get
            {
                float value = 0f;
                if (!BodyOrigin.isStar && BodyOrigin.orbit != null)
                {
                    value = Mathf.Max((float)BodyOrigin.orbit.radius * Mathf.Pow(10f, CelestialBodyMover.Instance.lineLengthExponent - 6f), 2f * (float)BodyOrigin.Radius); // scales on current altitude
                }
                //Util.Log($"CelestialBodyMover.Instance.settings.lineLength: {CelestialBodyMover.Instance.settings.lineLength}, value: {value}");
                return value;
                //return (float)BodyOrigin.orbit.radius * Mathf.Pow(10f, CelestialBodyMover.Instance.settings.lineLength - 6f);
            } 
        }

        DateTime _startDrawing;

        // Nullability: initialized in Start(), de-initialized in OnDestroy()
        GameObject _objLine = null;

        // Nullability: initialized in Start(), de-initialized in OnDestroy()
        LineRenderer _Line = null;

        const double AppearTime = 0.5;
        const double HideTime = 0.25;

        GUIStyle _styleLabel = null;

        string label;
        CelestialBody BodyOrigin;
        Func<Vector3d> PointDirection;

        Material orbitLines;

        enum DrawingState
        {
            Hidden,
            DrawingLinesAppearing,
            DrawingFullPicture,
            Hiding,
        };

        DrawingState _currentDrawingState = DrawingState.Hidden;

        private void OnStart() // non-unity
        {
            //if (!Util.MapViewEnabled())
            //{
            //    enabled = false;
            //    return;
            //}

            _objLine = new GameObject("Line");

            orbitLines = MapView.fetch.orbitLinesMaterial;

            _styleLabel = new GUIStyle
            {
                normal = { textColor = Color.white },
                alignment = TextAnchor.MiddleCenter,
                fontSize = 16,
                fontStyle = FontStyle.Bold,
            };
        }


        private void OnDestroy()
        {
            _currentDrawingState = DrawingState.Hidden;

            //Bin the objects
            _Line = null;

            Destroy(_objLine);
        }

        private void Log(string message) => Util.Log(message);

        internal void Draw(CelestialBody body, Func<Vector3d> vector, string label, Color color, bool visibilityChanged)
        {
            this.label = label;
            BodyOrigin = body;
            PointDirection = vector;

            OnStart();
            _Line = RenderUtils.InitLine(_objLine, color, 10, orbitLines); // this is so we can set the color here

            _startDrawing = DateTime.Now; // TODO, base this on currentUT instead, so it draws quicker with time warp?
            if (visibilityChanged) _currentDrawingState = DrawingState.DrawingLinesAppearing;
            else _currentDrawingState = DrawingState.DrawingFullPicture;

            //Util.Log($"Drawing line for {BodyOrigin?.displayName} with label {label}, visibilityChanged: {visibilityChanged}, _currentDrawingState: {_currentDrawingState}, _startDrawing: {_startDrawing}");
        }

        internal void Hide(bool visibilityChanged)
        {
            _startDrawing = DateTime.Now;
            if (visibilityChanged) _currentDrawingState = DrawingState.Hiding;
            else _currentDrawingState = DrawingState.Hidden;

            //Util.Log($"Hiding line for {BodyOrigin?.displayName} with label {label}, visibilityChanged: {visibilityChanged}, _currentDrawingState: {_currentDrawingState}, _startDrawing: {_startDrawing}");
        }

        void OnPreCull()
        {
            if (!Util.MapViewEnabled() || BodyOrigin == null || PointDirection == null || _currentDrawingState == DrawingState.Hidden)
            {
                if (_Line != null) _Line.enabled = false;
                return;
            }

            Vector3d dir = PointDirection().normalized;

            float pctDone;

            Vector3d center = BodyOrigin.transform.position; // TODO, change this to just BodyOrigin.position? the center seems to jiggle a lot if the body is moving fast and far away from origin in tracking station
            switch (_currentDrawingState)
            {
                case DrawingState.Hidden: // this shouldnt be possible
                    break;

                case DrawingState.DrawingLinesAppearing:
                    pctDone = (float)((DateTime.Now - _startDrawing).TotalSeconds / AppearTime);
                    if (pctDone >= 1)
                    {
                        _currentDrawingState = DrawingState.DrawingFullPicture;
                        _startDrawing = DateTime.Now;
                    }
                    pctDone = Mathf.Clamp01(pctDone);

                    Vector3d partialdir1 = dir * Mathf.Lerp(0, lineLength, pctDone);
                    RenderUtils.DrawLine(_Line, center, Vector3d.zero, partialdir1);
                    break;

                case DrawingState.DrawingFullPicture:
                    RenderUtils.DrawLine(_Line, center, Vector3d.zero, dir * lineLength);
                    break;

                case DrawingState.Hiding:
                    pctDone = (float)((DateTime.Now - _startDrawing).TotalSeconds / HideTime);
                    if (pctDone >= 1) { _currentDrawingState = DrawingState.Hidden; }
                    pctDone = Mathf.Clamp01(pctDone);

                    float partialLineLength = Mathf.Lerp(lineLength, 0, pctDone);

                    RenderUtils.DrawLine(_Line, center, Vector3d.zero, dir * partialLineLength);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            //Util.Log($"OnPreCull for {label}, drawing state: {_currentDrawingState}, pctDone: {pctDone}");
        }

        void OnGUI()
        {
            if (BodyOrigin == null || PointDirection == null || !Util.MapViewEnabled() || _currentDrawingState != DrawingState.DrawingFullPicture)
            { return; }

            Vector3 center = BodyOrigin.transform.position;
            Vector3 dir = PlanetariumCamera.Camera.WorldToScreenPoint(ScaledSpace.LocalToScaledSpace(center + lineLength * 1.05f * PointDirection().normalized));

            bool cameraNear = PlanetariumCamera.fetch.Distance < Math.Max(lineLength / 100f, PlanetariumCamera.fetch.minDistance);

            // checking z coordinate hides labels when they're behind the camera
            if (dir.z > 0 && cameraNear) GUI.Label(new Rect(dir.x - 50, Screen.height - dir.y - 15, 100, 30), label, _styleLabel);
        }
    }
}