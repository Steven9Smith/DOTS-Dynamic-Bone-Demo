using Egg.Extensions.Mathematics;
using Latios.Authoring;
using Latios.Kinemation;
using Latios.Kinemation.Authoring;
using Latios.Transforms;
using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using static Unity.Entities.SystemAPI;

namespace Dragons
{
#if LATIOS_TRANSFORMS_UNITY
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(VariableRateSimulationSystemGroup))]
#if DOTS_DYNAMIC_BONE
    [UpdateBefore(typeof(DOTSDynamicBone.Systems.DOTSDynamicBoneUpdateGroup))]
#else
    [UpdateBefore(typeof(TransformSystemGroup))]
#endif
#else
    [UpdateBefore(typeof(TransformSuperSystem))]
#endif
    public partial struct SingleClipPlayerSystem : ISystem
    {
        ComponentLookup<LocalTransform> GetLTR;
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            GetLTR = state.GetComponentLookup<LocalTransform>(true);
        }
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            //float t = (float)Time.ElapsedTime;
            GetLTR.Update(ref state);
            float t = (float)SystemAPI.Time.ElapsedTime;

/*            foreach ((var bones, var singleClip) in Query<DynamicBuffer<BoneReference>, RefRW<SingleClip>>().WithNone<SingleClipSettings>())
            {
                if (singleClip.ValueRO.Play)
                {
                    ref var clip = ref singleClip.ValueRO.blob.Value.clips[0];
                    var clipTime = clip.LoopToClipTime(t);
                    var lastClipTime = clip.LoopToClipTime(singleClip.ValueRO.lastTime);
                    bool clipWasReset = clipTime < lastClipTime;
                    if (clipWasReset && !singleClip.ValueRO.Loop)
                        singleClip.ValueRW.Play = false;
                    for (int i = 0; i < bones.Length; i++)
                    {
                        var boneSampledLocalTransform = clip.SampleBone(i, clipTime);
                        var newLT = boneSampledLocalTransform.ToMatrix4x4();
                        SetComponent(bones[i].bone, new LocalTransform
                        {
                            Position = newLT.Position(),
                            Rotation = newLT.Rotation(),
                            Scale = 1f// Unity.Mathematics.math.length(newLT.Scale())
                        });
                    }
                    singleClip.ValueRW.lastTime = t;
                }
            }*/

            foreach ((var bones, var singleClip, var singleClipSettings) in Query<DynamicBuffer<BoneReference>, RefRW<SingleClip>, RefRW<SingleClipSettings>>())
            {
                if (singleClipSettings.ValueRO.Play)
                {
                    ref var clip = ref singleClip.ValueRO.blob.Value.clips[0];
                    var clipTime = clip.LoopToClipTime(t);
                    var lastClipTime = clip.LoopToClipTime(singleClipSettings.ValueRO.lastTime);
                    bool clipWasReset = clipTime < lastClipTime;
                    lastClipTime = clipWasReset ? 0 : lastClipTime;
                    float3 position = float3.zero;
                    quaternion rotation = quaternion.identity;
                    for (int i = 0; i < bones.Length; i++)
                    {
                        var boneSampledLocalTransform = clip.SampleBone(i, clipTime);
                        var newLT = boneSampledLocalTransform.ToMatrix4x4();
                        if (i == 0 && singleClipSettings.ValueRO.ApplyRootMotion)
                        {
                            // need to handle when the animation resets!
                            var lt = GetLTR[bones[i].bone].ToMatrix();
                            var lastBoneSampledLocalTransform = clip.SampleBone(i, lastClipTime);
                            var oldLT = lastBoneSampledLocalTransform.ToMatrix4x4();
                            var diffRot = newLT.Rotation().Diff(oldLT.Rotation());
                            var diffPos = newLT.Position() - oldLT.Position();
                            lt = new float4x4(diffRot, diffPos).Add(lt);
                            position = lt.Position();
                            rotation = lt.Rotation();
                        }
                        else if (i > 0)
                        {
                            position = newLT.Position();
                            rotation = newLT.Rotation();
                        }
                        SetComponent(bones[i].bone, new LocalTransform
                        {
                            Position = position,
                            Rotation = rotation,
                            Scale = 1f// Unity.Mathematics.math.length(newLT.Scale())
                        });
                    }
                    singleClipSettings.ValueRW.lastTime = t;
                }
            }
        }
    }
    public struct SingleClip : IComponentData
    {
        public BlobAssetReference<SkeletonClipSetBlob> blob;
    }
    public struct SingleClipSettings : IComponentData
    {
        [MarshalAs(UnmanagedType.U1)]
        public bool ApplyRootMotion;
        public Entity Hips;
        public float lastTime;
        public bool Play;
        public bool Loop;
    }

}
namespace Dragons.Authoring
{
    [DisallowMultipleComponent]
    public class SingleClipAuthoring : MonoBehaviour
    {
        public AnimationClip clip;
        [Header("Root Motion Settings")]
        public bool ApplyRootMotion;
        public Transform Hips;
        [Header("Animation Settings")]
        public bool PlayOnStart;
        public bool Loop;
    }

    [TemporaryBakingType]
    struct SingleClipSmartBakeItem : ISmartBakeItem<SingleClipAuthoring>
    {
        SmartBlobberHandle<SkeletonClipSetBlob> blob;

        public bool Bake(SingleClipAuthoring authoring, IBaker baker)
        {
            Entity e = baker.GetEntity(TransformUsageFlags.Dynamic);
            baker.AddComponent<SingleClip>(e);
            if (authoring.ApplyRootMotion)
            {
                baker.AddComponent(e, new SingleClipSettings
                {
                    ApplyRootMotion = authoring.ApplyRootMotion,
                    Hips = authoring.Hips != null ? baker.GetEntity(authoring.Hips, TransformUsageFlags.Dynamic) : Entity.Null,
                    lastTime = 0,
                    Loop = authoring.Loop,
                    Play = authoring.PlayOnStart
                });
            }
            var clips = new NativeArray<SkeletonClipConfig>(1, Allocator.Temp);
            clips[0] = new SkeletonClipConfig { 
                clip = authoring.clip,
                settings = SkeletonClipCompressionSettings.kDefaultSettings
            };
            blob = baker.RequestCreateBlobAsset(baker.GetComponent<Animator>(), clips);
            return true;
        }

        public void PostProcessBlobRequests(EntityManager entityManager, Entity entity)
        {
            entityManager.SetComponentData(entity, new SingleClip
            {
                blob = blob.Resolve(entityManager)
            });
        }
    }

    class SingleClipBaker : SmartBaker<SingleClipAuthoring, SingleClipSmartBakeItem>
    {
    }
}