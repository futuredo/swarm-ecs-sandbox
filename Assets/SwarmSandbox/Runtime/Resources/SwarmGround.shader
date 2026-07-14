Shader "SwarmECS/Ground"
{
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry-10" }
        Pass
        {
            Cull Off
            ZWrite On

            CGPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "UnityCG.cginc"

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 worldXZ : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = UnityObjectToClipPos(input.positionOS);
                output.worldXZ = mul(unity_ObjectToWorld, input.positionOS).xz;
                return output;
            }

            fixed4 Frag(Varyings input) : SV_Target
            {
                float2 oneMeter = abs(frac(input.worldXZ + 0.5) - 0.5) / max(fwidth(input.worldXZ), 0.0001);
                float minorLine = 1.0 - saturate(min(oneMeter.x, oneMeter.y));
                float2 tenMeter = abs(frac(input.worldXZ / 10.0 + 0.5) - 0.5) / max(fwidth(input.worldXZ / 10.0), 0.0001);
                float majorLine = 1.0 - saturate(min(tenMeter.x, tenMeter.y));
                float axisX = 1.0 - saturate(abs(input.worldXZ.x) / max(fwidth(input.worldXZ.x) * 1.5, 0.0001));
                float axisZ = 1.0 - saturate(abs(input.worldXZ.y) / max(fwidth(input.worldXZ.y) * 1.5, 0.0001));

                float3 baseColor = float3(0.016, 0.024, 0.038);
                baseColor = lerp(baseColor, float3(0.04, 0.075, 0.105), minorLine * 0.42);
                baseColor = lerp(baseColor, float3(0.08, 0.16, 0.22), majorLine * 0.72);
                baseColor = lerp(baseColor, float3(0.12, 0.48, 0.62), max(axisX, axisZ) * 0.8);
                return float4(baseColor, 1.0);
            }
            ENDCG
        }
    }
    Fallback Off
}
