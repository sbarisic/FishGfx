#version 430

in vec4 frag_Clr;
in vec2 frag_UV;
in vec3 frag_Normal;
in vec3 frag_WorldPosition;
in vec4 frag_Light;

layout (location = 0) out vec4 OutColor;

uniform sampler2D Texture0;
uniform vec3 LightDirection;
uniform float AmbientLight;
uniform vec3 SunColor;
uniform float SunIntensity;
uniform float AlphaCutoff;
uniform vec3 uViewPosition;
uniform int FogEnabled;
uniform vec3 FogColor;
uniform float FogDensity;
uniform float LightMultiplier;
uniform mat4 uView;
uniform int uShadowEnabled;
uniform int uShadowCascadeCount;
uniform float uShadowStrength;
uniform float uShadowBlendFraction;
uniform int uShadowFilterRadius;
uniform mat4 uShadowMatrices[4];
uniform float uShadowSplits[4];
uniform float uShadowDepthRanges[4];
uniform sampler2D uShadowMaps[4];

float SampleShadowCascade(int cascade, vec3 worldPosition, vec3 normal, float nDotL)
{
	vec4 projected = uShadowMatrices[cascade] * vec4(worldPosition, 1.0);

	if (projected.w <= 0.0)
	{
		return 1.0;
	}

	vec3 shadowCoordinate = projected.xyz / projected.w;
	vec2 uv = shadowCoordinate.xy * 0.5 + 0.5;
	float receiverDepth = shadowCoordinate.z * 0.5 + 0.5;

	if (uv.x < 0.0 || uv.x > 1.0 || uv.y < 0.0 || uv.y > 1.0
		|| receiverDepth < 0.0 || receiverDepth > 1.0)
	{
		return 1.0;
	}

	float bias = clamp(
		(0.00035 + 0.0015 * (1.0 - nDotL)) * (uShadowDepthRanges[cascade] / 128.0),
		0.0002,
		0.0040
	);
	vec2 texel = 1.0 / vec2(textureSize(uShadowMaps[cascade], 0));
	float visible = 0.0;
	float samples = 0.0;

	for (int y = -uShadowFilterRadius; y <= uShadowFilterRadius; y++)
	{
		for (int x = -uShadowFilterRadius; x <= uShadowFilterRadius; x++)
		{
			vec2 sampleUv = uv + vec2(x, y) * texel;

			if (sampleUv.x < 0.0 || sampleUv.x > 1.0 || sampleUv.y < 0.0 || sampleUv.y > 1.0)
			{
				visible += 1.0;
			}
			else
			{
				float storedDepth = texture(uShadowMaps[cascade], sampleUv).r;
				visible += receiverDepth - bias <= storedDepth ? 1.0 : 0.0;
			}

			samples += 1.0;
		}
	}

	return visible / samples;
}

float SampleSunVisibility(vec3 worldPosition, vec3 normal, float nDotL)
{
	if (uShadowEnabled == 0 || uShadowCascadeCount == 0)
	{
		return 1.0;
	}

	float viewDepth = -(uView * vec4(worldPosition, 1.0)).z;
	int cascade = uShadowCascadeCount - 1;

	for (int index = 0; index < uShadowCascadeCount; index++)
	{
		if (viewDepth <= uShadowSplits[index])
		{
			cascade = index;
			break;
		}
	}

	float visibility = SampleShadowCascade(cascade, worldPosition, normal, nDotL);

	if (cascade + 1 < uShadowCascadeCount)
	{
		float blendWidth = max(uShadowDepthRanges[cascade] * uShadowBlendFraction, 0.0001);
		float blendStart = uShadowSplits[cascade] - blendWidth;
		float blend = clamp((viewDepth - blendStart) / blendWidth, 0.0, 1.0);

		if (blend > 0.0)
		{
			float nextVisibility = SampleShadowCascade(cascade + 1, worldPosition, normal, nDotL);
			visibility = mix(visibility, nextVisibility, blend);
		}
	}

	return mix(1.0, visibility, uShadowStrength);
}

void main()
{
	vec4 sampled = texture(Texture0, frag_UV) * frag_Clr;

	if (AlphaCutoff >= 0.0 && sampled.a < AlphaCutoff)
	{
		discard;
	}

	vec3 normal = normalize(frag_Normal);
	float diffuse = max(dot(normal, normalize(-LightDirection)), 0.0);
	float shadowVisibility = SampleSunVisibility(frag_WorldPosition, normal, diffuse);
	vec3 skyAmbient = frag_Light.a * SunColor * SunIntensity * AmbientLight;
	vec3 skyDirect = frag_Light.a * SunColor * SunIntensity
		* diffuse * (1.0 - AmbientLight) * shadowVisibility;
	vec3 skyLight = skyAmbient + skyDirect;
	vec3 lighting = max(frag_Light.rgb, skyLight);
	vec3 litColor = sampled.rgb * lighting * LightMultiplier;
	float fogFactor = FogEnabled != 0
		? clamp(1.0 - exp(-FogDensity * distance(uViewPosition, frag_WorldPosition)), 0.0, 1.0)
		: 0.0;
	OutColor = vec4(mix(litColor, FogColor, fogFactor), sampled.a);
}
