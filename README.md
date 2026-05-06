![Toolbar Icon](https://github.com/Clayell/CelestialBodyMover/blob/master/CelestialBodyMover.svg)

[![GitHub Downloads (all assets, latest release)](https://img.shields.io/github/downloads/Clayell/CelestialBodyMover/latest/total?label=downloads%20(latest))](https://github.com/Clayell/CelestialBodyMover/releases/latest)
[![GitHub Downloads (all assets, all releases)](https://img.shields.io/github/downloads/Clayell/CelestialBodyMover/total?label=downloads%20(total))](https://github.com/Clayell/CelestialBodyMover/releases)

# Celestial Body Mover (CBM)

###### Put your hands on the rails of the body orbits and push them aside. - [steamroller](https://discord.com/channels/601452466017665040/601458726192414741/1493350537108787201)

### Provides a way for the player to move any celestial body through vessel thrust, gravity, or impacts.

Celestial Body Mover (CBM) lets vessels move and change the spin* of any* body in KSP in 3 different ways:

* Aiming down at the body with a rocket engine
	* CBM allows you to keep your vessel stationary while above the surface of the body, and allows you to include the body's mass in your Delta-V calculations
* Using your vessel's gravity to pull the body in your direction
	* This does not change the spin of the body, as tidal effects are not modelled
* Smashing into the body via an inelastic collision

###### *The body's rotational axis is fixed due to KSP constraints, but the rotation along this axis can be fully changed and even reversed.

###### *The host body of a system (e.g. the star) cannot be moved or have its spin changed

Celestial Body Mover also provides you with lines in the map view that display the body's orbit vectors (and your force vector, if available), information about the vessel, body, and the body's orbit, and options to allow the body to move into a different sphere of influence.

## Links

Downloading through [CKAN](https://github.com/KSP-CKAN/CKAN) is highly recommended.

Forum Thread: https://forum.kerbalspaceprogram.com/topic/230493-v1000-celestial-body-mover-cbm/

SpaceDock: https://spacedock.info/mod/4245/Celestial%20Body%20Mover%20(CBM)

Source Code: https://github.com/Clayell/CelestialBodyMover/tree/master/Source

## Mod Relationships

#### Required:
* ToolbarController
* ClickThroughBlocker
* Harmony2

#### Compatible With:
* All solar systems (file an [issue](https://github.com/Clayell/CelestialBodyMover/issues) if your solar system does not work)

#### Incompatible with:
* Principia

---

### Author: [Clayel](https://github.com/Clayell)

### Special Thanks To:
* [Nazfib](https://github.com/Nazfib) (Orbit/Angle Renderer from [TWP2](https://github.com/Nazfib/TransferWindowPlanner2))
* [siimav](https://github.com/siimav) ([Tooltips from RP-1](https://github.com/KSP-RO/RP-1/blob/master/Source/RP0/UI/Tooltip.cs))

### License:
This mod is subject to the MIT license. (see [LICENSE.md](https://github.com/Clayell/CelestialBodyMover/blob/master/LICENSE.md))

### Media:

#### Images:
![Thrust](https://i.imgur.com/P7HrwAP.png)

![Gravity](https://i.imgur.com/7WDKTdr.png)

![Impact](https://i.imgur.com/FKTGo1D.png)

#### Videos:
[Moving bodies with thrust](https://youtu.be/W7Jd040uetA)

[Moving bodies with gravity](https://youtu.be/EJK8Sxqn6d8)

[Moving bodies with an impact](https://youtu.be/W6uGWHxCu3w)