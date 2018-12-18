#version 410

layout (location = 0) in vec4 Clr;
layout (location = 1) in vec2 UV;

uniform sampler2D Texture;

layout (location = 0) out vec4 OutClr;

void main() {
	OutClr = texture(Texture, UV) * Clr;

	//OutClr = Clr;
}