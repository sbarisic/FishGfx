#version 400

in vec4 gColor;

layout (location = 0) out vec4 outColor;

void main()
{
	outColor = gColor;
}
