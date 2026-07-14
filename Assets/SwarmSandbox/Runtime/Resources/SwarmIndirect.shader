Shader "SwarmECS/IndirectAgent"
{
    Properties
    {
        _AgentScale ("Agent Scale", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            Cull Back
            ZWrite On

            CGPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            #define UNITY_INDIRECT_DRAW_ARGS IndirectDrawIndexedArgs
            #include "UnityIndirect.cginc"

            struct AgentGpuData
            {
                float4 positionVelocity;
                float4 metadata;
            };

            StructuredBuffer<AgentGpuData> _AgentData;
            float _AgentScale;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                uint instanceID : SV_InstanceID;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float4 color : COLOR0;
            };

            float3 GroupColor(uint group)
            {
                if (group == 0) return float3(0.12, 0.82, 1.00);
                if (group == 1) return float3(1.00, 0.31, 0.47);
                if (group == 2) return float3(0.98, 0.76, 0.16);
                return float3(0.42, 1.00, 0.45);
            }

            Varyings Vert(Attributes input)
            {
                InitIndirectDrawArgs(0);
                Varyings output;
                uint agentIndex = GetIndirectInstanceID(input.instanceID);
                AgentGpuData agent = _AgentData[agentIndex];
                float2 velocity = agent.positionVelocity.zw;
                float speed = length(velocity);
                float2 forward = speed > 0.001 ? velocity / speed : float2(0.0, 1.0);
                float2 right = float2(forward.y, -forward.x);
                float radiusScale = max(0.25, agent.metadata.y) * _AgentScale;

                float3 local = input.positionOS.xyz * radiusScale;
                float3 worldPosition;
                worldPosition.xz = agent.positionVelocity.xy + right * local.x + forward * local.z;
                worldPosition.y = local.y + 0.04;

                float3 localNormal = input.normalOS;
                output.normalWS = normalize(float3(
                    right.x * localNormal.x + forward.x * localNormal.z,
                    localNormal.y,
                    right.y * localNormal.x + forward.y * localNormal.z));
                output.positionCS = mul(UNITY_MATRIX_VP, float4(worldPosition, 1.0));
                output.color = float4(GroupColor((uint)agent.metadata.x), saturate(speed / max(0.001, agent.metadata.z)));
                return output;
            }

            fixed4 Frag(Varyings input) : SV_Target
            {
                float3 lightDirection = normalize(float3(0.35, 0.82, 0.44));
                float light = 0.34 + 0.66 * saturate(dot(normalize(input.normalWS), lightDirection));
                float motionGlow = 0.82 + input.color.a * 0.18;
                return float4(input.color.rgb * light * motionGlow, 1.0);
            }
            ENDCG
        }
    }
    Fallback Off
}
