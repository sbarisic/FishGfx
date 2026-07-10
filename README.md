# FishGfx
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
