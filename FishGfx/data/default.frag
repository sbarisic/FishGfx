#version 450

layout (location = 0) in vec4 Clr;
layout (location = 1) in vec2 UV;

uniform sampler2D Texture;

out vec4 OutClr;

void main() {
	vec4 TexClr = texture2D(Texture, UV);
	
	OutClr = Clr * TexClr;
}