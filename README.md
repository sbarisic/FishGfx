# FishGfx

## Function node graphs

FishGfx can expose explicitly marked methods on a static class as strongly typed nodes:

```csharp
using FishGfx.NodeGraph;

static class MathNodes {
    [NodeFunction("Constant")]
    public static float Constant([NodeBody] float value = 1) => value;

    [NodeFunction]
    public static float Add(float a, float b) => a + b;
}

var registry = new NodeFunctionRegistry();
registry.Register(typeof(MathNodes));
```

Ordinary parameters become typed input ports, `[NodeBody]` parameters become editable node values, and return values become outputs. Named `ValueTuple` returns produce multiple output ports. `FunctionNodeEvaluator` evaluates a connected `FunctionNodeGraph` on demand while reporting cycles and per-node errors.

Layouts can be persisted with `NodeGraphJson.Serialize` / `SaveFile` and restored against an existing registry with `Deserialize` / `LoadFile`. `DeserializeAndEvaluate` and `LoadAndEvaluateFile` validate and execute layouts directly without loading functions that were not explicitly registered by the host.

The node editor stores its interactive layout as `node-layout.json` beside the executable. Use Ctrl+S to save, Ctrl+O to reload, or execute a saved layout without opening a window:

```powershell
dotnet FishGfx.NodeEditor.dll --execute node-layout.json
```
SFML but better. OpenGL 4, GLFW, .NET 10, and Silk.NET.

https://github.com/Chman/Glfw.Net
https://github.com/dotnet/Silk.NET

## Modern build

The supported modern configuration is Windows x64 with the .NET 10 SDK:

```powershell
dotnet build FishGfx.Modern.sln
dotnet test FishGfx.Tests/FishGfx.Tests.csproj
dotnet run --project FishGfx.SmokeTest/FishGfx.SmokeTest.csproj
```

The other solutions and demo projects remain on .NET Framework pending separate migrations. Intel RealSense support has been removed.

# Completed

* Automatic OpenGL context creation from 4.6 down to 4.0
* Textures, RenderTextures
* Vertex arrays
* Shaders
* Framebuffers
* 2D and 3D meshes
* Cameras
* Terrain, 3D mesh from heightmap
* Basic 2D drawing library
	* Points and thick lines/line strips
	* Filled, outlined, and textured rectangles
	* Filled, outlined, and textured rounded rectangles with per-corner radii
	* Stretched nine-patch textures with fixed source-pixel borders
	* Filled, outlined, and textured circles and ellipses
	* Filled and outlined complete rings and annular sectors
	* Stroked quadratic and cubic Bézier curves
	
# TODO

* Sprites
* Tile maps
* Text
	* Signed distance field
	* Classic font atlas
	* Loading glyphs with Freetype?
* Scissoring
* Stencil buffer
* Command buffers
* Examples

# Examples

![](https://raw.githubusercontent.com/sbarisic/FishGfx/master/pictures/zsY9ve2AN3.png)
