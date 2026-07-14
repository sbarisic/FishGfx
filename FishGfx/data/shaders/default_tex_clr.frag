#version 400

in vec4 vColor;
in vec2 vUv;

uniform sampler2D uTexture;
uniform float uAlphaCutoff;

layout (location = 0) out vec4 outColor;

void main()
{
	vec4 fragmentColor = texture(uTexture, vUv) * vColor;

	if (fragmentColor.a < uAlphaCutoff)
	{
		discard;
	}

	outColor = fragmentColor;
}
