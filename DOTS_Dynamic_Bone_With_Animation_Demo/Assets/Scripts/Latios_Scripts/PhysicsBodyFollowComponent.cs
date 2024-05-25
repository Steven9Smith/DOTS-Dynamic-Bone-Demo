using Egg.Extensions.Mathematics;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace UnityNexus
{
    public class PhysicsBodyFollowComponent : MonoBehaviour
    {
        public Transform FollowTarget;

        public float3 PositionOffset;
        public quaternion RotationOffset;

        public class PhysicsBodyFollowComponentBaker : Baker<PhysicsBodyFollowComponent>
        {
            public override void Bake(PhysicsBodyFollowComponent authoring)
            {
                if (authoring.FollowTarget != null)
                {
                    AddComponent(GetEntity(TransformUsageFlags.Dynamic), new PBFTData
                    {
                        follow = GetEntity(authoring.FollowTarget, TransformUsageFlags.Dynamic),
                        positionOffset = authoring.PositionOffset,
                        rotationOffset = authoring.RotationOffset,
                    });
                }
                else Debug.LogError("Failed to setup Follow, please check the transform.");
            }
        }

        private void OnValidate()
        {
            if (FollowTarget != null)
            {
                PositionOffset = FollowTarget.position - transform.position;
                RotationOffset = FollowTarget.rotation.Diff(transform.rotation); //transform.rotation.Add(math.inverse(FollowTarget.rotation));
            }
        }

        // Start is called before the first frame update
        void Start()
        {

        }

        // Update is called once per frame
        void Update()
        {

        }
    }

    public struct PBFTData : IComponentData
    {
        public Entity follow;
        public float3 positionOffset;
        public quaternion rotationOffset;
    }

    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(Unity.Transforms.TransformSystemGroup))]
    [UpdateBefore(typeof(CompanionGameObjectUpdateTransformSystem))]
    public partial struct PhysicsBodyFollowTransform : ISystem
    {
        ComponentLookup<LocalTransform> GetLT;
        ComponentLookup<LocalToWorld> GetLTW;
        public void OnCreate(ref SystemState state)
        {
            GetLT = SystemAPI.GetComponentLookup<LocalTransform>(false);
            GetLTW = SystemAPI.GetComponentLookup<LocalToWorld>(false);
        }

        public void OnUpdate(ref SystemState state)
        {
            GetLT.Update(ref state);
            GetLTW.Update(ref state);
            state.Dependency = new MoveBodyJob
            {
                GetLT = GetLT,
                GetLTW = GetLTW
            }.Schedule(state.Dependency);
        }
        public partial struct MoveBodyJob : IJobEntity
        {
            public ComponentLookup<LocalTransform> GetLT;
            public ComponentLookup<LocalToWorld> GetLTW;
            public void Execute(Entity e,ref PBFTData data)
            {
                var _lt = GetLT[data.follow];
                var _ltw = GetLTW[data.follow];
                var l = new float4x4(data.rotationOffset.Add(_ltw.Rotation), _ltw.Position + data.positionOffset);
                _lt = new LocalTransform
                {
                    Scale = _lt.Scale,
                    Position = l.Position(),
                    Rotation = l.Rotation()
                };
                GetLT[e] = _lt;

            }
        }
    }
}
