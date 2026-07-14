#version 400

in vec4 vColor;
in vec2 vUv;

uniform sampler2D uTexture;

layout (location = 0) out vec4 outColor;

void main()
{
	outColor = texture(uTexture, vUv) * vColor;
}
