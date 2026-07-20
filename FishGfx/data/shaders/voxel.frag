#version 430

in vec4 frag_Clr;
in vec2 frag_UV;
in vec3 frag_Normal;
in vec4 frag_Tangent;
in vec3 frag_WorldPosition;
in vec4 frag_Light;
in float frag_WaveAmplitude;

layout (location = 0) out vec4 OutColor;

uniform sampler2D Texture0;
uniform sampler2D NormalTexture;
uniform sampler2D SpecularTexture;
uniform sampler2D RoughnessTexture;
uniform int SurfaceMapsEnabled;
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
uniform float uShadowMapDepthRanges[4];
uniform float uShadowWorldTexelSizes[4];
uniform sampler2D uShadowMaps[4];

vec3 SafeNormalize(vec3 value, vec3 fallback)
{
	float lengthSquared = dot(value, value);
	return lengthSquared > 0.0000001
		? value * inversesqrt(lengthSquared)
		: fallback;
}

bool TryBuildDerivativeTangent(
	vec3 geometricNormal,
	out vec3 tangent,
	out float handedness)
{
	vec3 positionDx = dFdx(frag_WorldPosition);
	vec3 positionDy = dFdy(frag_WorldPosition);
	vec2 uvDx = dFdx(frag_UV);
	vec2 uvDy = dFdy(frag_UV);
	float determinant = uvDx.x * uvDy.y - uvDx.y * uvDy.x;

	if (abs(determinant) <= 0.0000001)
	{
		tangent = vec3(0.0);
		handedness = 1.0;
		return false;
	}

	float inverseDeterminant = 1.0 / determinant;
	vec3 candidateTangent = (positionDx * uvDy.y - positionDy * uvDx.y)
		* inverseDeterminant;
	vec3 candidateBitangent = (positionDy * uvDx.x - positionDx * uvDy.x)
		* inverseDeterminant;
	candidateTangent -= geometricNormal * dot(geometricNormal, candidateTangent);
	float tangentLengthSquared = dot(candidateTangent, candidateTangent);
	float bitangentLengthSquared = dot(candidateBitangent, candidateBitangent);

	if (tangentLengthSquared <= 0.0000001 || bitangentLengthSquared <= 0.0000001)
	{
		tangent = vec3(0.0);
		handedness = 1.0;
		return false;
	}

	tangent = candidateTangent * inversesqrt(tangentLengthSquared);
	handedness = dot(cross(geometricNormal, tangent), candidateBitangent) < 0.0
		? -1.0
		: 1.0;
	return true;
}

float SampleShadowCascade(int cascade, vec3 worldPosition, vec3 normal, float nDotL)
{
	float slope = 1.0 - clamp(nDotL, 0.0, 1.0);
	float worldTexelSize = uShadowWorldTexelSizes[cascade];
	float normalOffset = min(worldTexelSize * 1.25 * slope, 0.2);
	vec3 receiverPosition = worldPosition + normal * normalOffset;
	vec4 projected = uShadowMatrices[cascade] * vec4(receiverPosition, 1.0);

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

	float worldBias = 0.01 + worldTexelSize * 0.1 * slope;
	float bias = clamp(
		worldBias * 0.5 / max(uShadowMapDepthRanges[cascade], 1.0),
		0.000002,
		0.0001
	);
	vec2 texel = 1.0 / vec2(textureSize(uShadowMaps[cascade], 0));
	vec2 minimumUv = texel * 0.5;
	vec2 maximumUv = vec2(1.0) - minimumUv;
	float visible = 0.0;
	float samples = 0.0;

	for (int y = -uShadowFilterRadius; y <= uShadowFilterRadius; y++)
	{
		for (int x = -uShadowFilterRadius; x <= uShadowFilterRadius; x++)
		{
			vec2 sampleUv = clamp(
				uv + vec2(x, y) * texel,
				minimumUv,
				maximumUv
			);
			float storedDepth = texture(uShadowMaps[cascade], sampleUv).r;
			visible += receiverDepth - bias <= storedDepth ? 1.0 : 0.0;

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

	vec3 geometricNormal = SafeNormalize(frag_Normal, vec3(0.0, 1.0, 0.0));
	vec3 normal = geometricNormal;
	float tangentLengthSquared = dot(frag_Tangent.xyz, frag_Tangent.xyz);
	bool useSurfaceMaps = SurfaceMapsEnabled != 0
		&& abs(frag_Tangent.w) > 0.5
		&& tangentLengthSquared > 0.0000001;

	if (useSurfaceMaps)
	{
		vec3 tangent;
		float handedness;
		bool derivativeTangent = frag_WaveAmplitude > 0.0
			&& TryBuildDerivativeTangent(geometricNormal, tangent, handedness);
		if (!derivativeTangent)
		{
			tangent = frag_Tangent.xyz
				- geometricNormal * dot(geometricNormal, frag_Tangent.xyz);
			tangent = SafeNormalize(tangent, vec3(0.0));
			handedness = sign(frag_Tangent.w);
		}
		useSurfaceMaps = dot(tangent, tangent) > 0.0000001;
		vec3 bitangent = SafeNormalize(
			cross(geometricNormal, tangent),
			vec3(0.0)
		) * handedness;
		vec3 tangentNormal = texture(NormalTexture, frag_UV).xyz * 2.0 - 1.0;
		tangentNormal = SafeNormalize(tangentNormal, vec3(0.0, 0.0, 1.0));
		if (useSurfaceMaps)
		{
			normal = SafeNormalize(
				mat3(tangent, bitangent, geometricNormal) * tangentNormal,
				geometricNormal
			);
		}
	}

	vec3 lightDirection = SafeNormalize(-LightDirection, vec3(0.0, 1.0, 0.0));
	float diffuse = max(dot(normal, lightDirection), 0.0);
	float geometricDiffuse = max(dot(geometricNormal, lightDirection), 0.0);
	float shadowVisibility = SampleSunVisibility(
		frag_WorldPosition,
		geometricNormal,
		geometricDiffuse
	);
	vec3 skyAmbient = frag_Light.a * SunColor * SunIntensity * AmbientLight;
	vec3 skyDirect = frag_Light.a * SunColor * SunIntensity
		* diffuse * (1.0 - AmbientLight) * shadowVisibility;
	vec3 skyLight = skyAmbient + skyDirect;
	vec3 lighting = max(frag_Light.rgb, skyLight);
	vec3 litColor = sampled.rgb * lighting * LightMultiplier;

	if (useSurfaceMaps && diffuse > 0.0)
	{
		float specularIntensity = texture(SpecularTexture, frag_UV).r;
		float roughness = texture(RoughnessTexture, frag_UV).r;
		float exponent = exp2(mix(8.0, 2.0, roughness));
		vec3 viewDirection = SafeNormalize(
			uViewPosition - frag_WorldPosition,
			geometricNormal
		);
		vec3 halfVector = lightDirection + viewDirection;
		float halfLengthSquared = dot(halfVector, halfVector);
		vec3 halfDirection = halfLengthSquared > 0.0000001
			? halfVector * inversesqrt(halfLengthSquared)
			: vec3(0.0);
		float highlight = halfLengthSquared > 0.0000001
			? pow(max(dot(normal, halfDirection), 0.0), exponent)
			: 0.0;
		litColor += SunColor * SunIntensity * (1.0 - AmbientLight)
			* frag_Light.a * shadowVisibility * specularIntensity * highlight
			* diffuse * LightMultiplier;
	}
	float fogFactor = FogEnabled != 0
		? clamp(1.0 - exp(-FogDensity * distance(uViewPosition, frag_WorldPosition)), 0.0, 1.0)
		: 0.0;
	OutColor = vec4(mix(litColor, FogColor, fogFactor), sampled.a);
}
