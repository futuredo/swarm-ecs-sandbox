Shader "SwarmECS/SATObstacle"
{
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            Cull Back
            ZWrite On

            CGPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 worldPosition : TEXCOORD1;
            };

            Varyings Vert(Attributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                Varyings output;
                output.positionCS = UnityObjectToClipPos(input.positionOS);
                output.normalWS = UnityObjectToWorldNormal(input.normalOS);
                output.worldPosition = mul(unity_ObjectToWorld, input.positionOS).xyz;
                return output;
            }

            fixed4 Frag(Varyings input) : SV_Target
            {
                float edgePulse = 0.86 + 0.14 * sin((_Time.y * 1.8) + input.worldPosition.x * 0.08 + input.worldPosition.z * 0.08);
                float light = 0.35 + 0.65 * saturate(dot(normalize(input.normalWS), normalize(float3(0.35, 0.82, 0.44))));
                float3 color = float3(0.43, 0.12, 0.17) * edgePulse * light;
                return float4(color, 1.0);
            }
            ENDCG
        }
    }
    Fallback Off
}
