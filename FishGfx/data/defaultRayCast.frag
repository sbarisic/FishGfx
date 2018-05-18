#version 410

layout (location = 0) in vec4 Clr;
layout (location = 1) in vec2 UV;

uniform sampler2D Texture;

layout (location = 0) out vec4 OutClr;
layout (location = 1) out vec4 RayClr;

void main() {
	vec4 TexClr = texture(Texture, UV);

	OutClr = TexClr;
	RayClr = Clr;
}