#version 400

in vec4 vColor;
in vec2 vUv;

uniform sampler2D uTexture;
uniform float uAlphaCutoff;
uniform float uSdfPixelRange;

layout (location = 0) out vec4 outColor;

void main()
{
	float distance = texture(uTexture, vUv).r;
	vec2 unitRange = vec2(uSdfPixelRange) / vec2(textureSize(uTexture, 0));
	vec2 screenTextureSize = vec2(1.0) / max(fwidth(vUv), vec2(0.000001));
	float screenPixelRange = max(0.5 * dot(unitRange, screenTextureSize), 1.0);
	float coverage = clamp(screenPixelRange * (distance - 0.5) + 0.5, 0.0, 1.0);
	coverage = smoothstep(0.15, 0.85, coverage);

	if (coverage <= 0.001)
	{
		discard;
	}

	float alpha = vColor.a * coverage;

	if (alpha < uAlphaCutoff)
	{
		discard;
	}

	outColor = vec4(vColor.rgb, alpha);
}
