#version 410

layout (location = 0) in vec4 Clr;
layout (location = 1) in vec2 UV;

uniform sampler2D Texture;
uniform float AlphaTest;

layout (location = 0) out vec4 OutClr;

void main() {
	vec4 Fragment = texture(Texture, UV) * Clr;

	if (Fragment.a < AlphaTest)
		discard;

	OutClr = Fragment;
}