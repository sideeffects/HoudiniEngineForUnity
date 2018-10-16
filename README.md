# Houdini Engine for Unity
Houdini Engine for Unity is a Unity plug-in that allows deep integration of
Houdini technology into Unity through the use of Houdini Engine.

This plug-in brings Houdini's powerful and flexible procedural workflow into
Unity through Houdini Digital Assets. Artists can interactively adjust the
asset's parameters inside Unity, and use Unity geometries as an asset's inputs.
Houdini's procedural engine will then "cook" the asset and the results will be
available right inside Unity.

The easiest way for artists to access the plug-in is to download the latest
production build of Houdini and install the Unity plug-in along with the Houdini interactive software.
Houdini Digital Assets created in Houdini can then be loaded into Unity through the plug-in. 
A growing library of Digital Assets for use in Unity will be available at the [Orbolt Smart 3D Asset
Store](http://www.orbolt.com/unity).

For more information:

* [Houdini Engine for Unity Product Info](https://www.sidefx.com/products/houdini-engine/unity-plug-in/)
* [Houdini Enigne for Unity Documentation](https://www.sidefx.com/docs/unity/index.html)
* [FAQ](https://www.sidefx.com/faq/houdini-engine-faq/)

For support and reporting bugs:

* [SideFX Houdini Engine for Unity forum](https://www.sidefx.com/forum/50/)
* [Bug Submission](https://www.sidefx.com/bugs/submit/)

## Supported Unity versions
Currently, the supported Unity versions are:

* 5.6 and newer

## Installing from Source
1. Fork this repository to your own Github account using the Fork button at the top.
1. Clone the forked repository to your file system.
1. Download and install the correct build of Houdini. You must have the exact build number and version as HOUDINI_MAJOR, HOUDINI_MINOR, and HOUDINI_BUILD int values in Plugins/HoudiniEngineUnity/Scripts/HEU_HoudiniVersion.cs. You can get the correct build from: http://www.sidefx.com/download/daily-builds (you might need to wait for the build to finish and show up if you're updating to the very latest version of the plugin)
1. Open a project in Unity. Note that if a previous version of the plugin exists in the project (usually located at Assets/Plugins/HoudiniEngineUnity), then you'll need to remove it from the Unity project. To do so, in Unity, in the Project browser, right-click on HoudiniEngineUnity folder in Assets/Plugins and select Delete.
1. Copy the Plugins/HoudiniEngineUnity folder from the cloned repository from step 2, and paste it into your Unity project's Assets/Plugins folder. If the Plugins folder exists, you can simply merge with it.
1. Restart Unity.
1. Ensure Houdini Engine loaded successfully by going to the "HoudiniEngine" top menu and selecting "Installation Info" and making sure all the versions match.
