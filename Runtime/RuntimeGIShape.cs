using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics.Authoring;

namespace RuntimeGI
{
    [UnityEngine.RequireComponent(typeof(PhysicsShapeAuthoring))]
    public class RuntimeGIShape : UnityEngine.MonoBehaviour, IConvertGameObjectToEntity
    {
        public float EdgeMargins;
        public RuntimeGIMaterial Material;

        public void Convert(Entity entity, EntityManager dstManager, GameObjectConversionSystem conversionSystem)
        {
            //return;

            dstManager.AddComponentData(entity, Material);
            dstManager.AddComponent<RuntimeGIShapeState>(entity);
            var giSystem = World.DefaultGameObjectInjectionWorld.GetOrCreateSystem<RuntimeGISystem>();
            giSystem.AddShape(entity, this);
        }

        public void ConvertOLD(Entity entity, EntityManager dstManager, GameObjectConversionSystem conversionSystem)
        {
            var albedoV4 = (UnityEngine.Vector4)Material.Albedo;
            var points = GenerateLitPointsFromShapeOLD(GetComponent<PhysicsShapeAuthoring>(), Material.Resolution, EdgeMargins, (UnityEngine.Vector3)albedoV4);
            var entities = new NativeArray<Entity>(points.Length, Allocator.Temp);

            var archetype = dstManager.CreateArchetype(typeof(LitPointData), typeof(SettingUpLitPoint));
            dstManager.CreateEntity(archetype, entities);

            using (var query = dstManager.CreateEntityQuery(typeof(LitPointData), typeof(SettingUpLitPoint)))
            {
                query.CopyFromComponentDataArray(points.AsArray());
                dstManager.RemoveComponent(query, typeof(SettingUpLitPoint));
            }

            dstManager.AddComponentData(entity, Material);
            //dstManager.AddBuffer<RuntimeGILitPoints>(entity).AddRange(entities.Reinterpret<RuntimeGILitPoints>());
        }

        public static void GenerateLitPointsFromShape(NativeSlice<LitPoint> points, int startIndex, NativeList<LitPolygon> pointRanges, PhysicsShapeAuthoring physShape, int2 resolution, float edgeMargins)
        {
            if (physShape != null)
            {
                switch (physShape.ShapeType)
                {
                    case ShapeType.Box:
                        break;
                    case ShapeType.Capsule:
                        break;
                    case ShapeType.Sphere:
                        break;
                    case ShapeType.Cylinder:
                        break;
                    case ShapeType.Plane:
                        {
                            physShape.GetPlaneProperties(out var center, out var size, out var rot);

                            var scale = (float3)physShape.transform.lossyScale;
                            var scaleXY = new float2(scale.x, scale.z);
                            var radQuat = quaternion.LookRotationSafe(physShape.transform.forward, physShape.transform.up);

                            var defaultOffset = new float4(size.x / 2f, 0, size.x / 2f, 0);
                            var localToWorld = float4x4.TRS((float3)physShape.transform.position + center, math.mul(radQuat, rot), scale);
                            var rotation = math.mul(radQuat, rot);
                            var marginSize = size - (edgeMargins * 2 / scaleXY);

                            var index = 0;
                            for (int y = 0; y < resolution.y; y++)
                            {
                                var normY = y / (resolution.y - 1f);
                                for (int x = 0; x < resolution.x; x++)
                                {
                                    var normX = x / (resolution.x - 1f);

                                    var localPos = new float4(normX * marginSize.x, 0, normY * marginSize.y, 1) + new float4(edgeMargins / scaleXY.x, 0, edgeMargins / scaleXY.y, 0);
                                    var worldPos = math.mul(localToWorld, localPos - defaultOffset);

                                    points[index] = new LitPoint
                                    {
                                        Position = worldPos.xyz,
                                        Rotation = rotation,
                                    };
                                    index++;
                                }
                            }

                            pointRanges.Add(new LitPolygon
                            {
                                Start = startIndex,
                                Length = index,
                                Width = resolution.x,
                                Height = resolution.y,
                            });
                        }
                        break;
                    case ShapeType.ConvexHull:
                        break;
                    case ShapeType.Mesh:
                        break;
                    default:
                        break;
                }
            }
        }

        public static NativeList<LitPointData> GenerateLitPointsFromShapeOLD(PhysicsShapeAuthoring physShape, int2 resolution, float edgeMargins, float3 albedo)
        {
            var points = new NativeList<LitPointData>(Allocator.Temp);
            if (physShape != null)
            {
                switch (physShape.ShapeType)
                {
                    case ShapeType.Box:
                        break;
                    case ShapeType.Capsule:
                        break;
                    case ShapeType.Sphere:
                        break;
                    case ShapeType.Cylinder:
                        break;
                    case ShapeType.Plane:
                        {
                            physShape.GetPlaneProperties(out var center, out var size, out var rot);

                            var scale = (float3)physShape.transform.lossyScale;
                            var scaleXY = new float2(scale.x, scale.z);
                            var radQuat = quaternion.LookRotationSafe(physShape.transform.forward, physShape.transform.up);

                            var defaultOffset = new float4(size.x / 2f, 0, size.x / 2f, 0);
                            var localToWorld = float4x4.TRS((float3)physShape.transform.position + center, math.mul(radQuat, rot), scale);
                            var rotation = math.mul(radQuat, rot);
                            var marginSize = size - (edgeMargins * 2 / scaleXY);

                            for (int y = 0; y < resolution.y; y++)
                            {
                                var normY = y / (resolution.y - 1f);
                                for (int x = 0; x < resolution.x; x++)
                                {
                                    var normX = x / (resolution.x - 1f);

                                    var localPos = new float4(normX * marginSize.x, 0, normY * marginSize.y, 1) + new float4(edgeMargins / scaleXY.x, 0, edgeMargins / scaleXY.y, 0);
                                    var worldPos = math.mul(localToWorld, localPos - defaultOffset);

                                    points.Add(new LitPointData
                                    {
                                        Albedo = albedo,
                                        Position = worldPos.xyz,
                                        Rotation = rotation,
                                    });
                                }
                            }
                        }
                        break;
                    case ShapeType.ConvexHull:
                        break;
                    case ShapeType.Mesh:
                        break;
                    default:
                        break;
                }
            }

            return points;
        }

        private void OnDrawGizmosSelected()
        {
            var physShape = GetComponent<PhysicsShapeAuthoring>();
            var points = GenerateLitPointsFromShapeOLD(physShape, Material.Resolution, EdgeMargins, default);

            UnityEngine.Gizmos.color = UnityEngine.Color.red;
            for (int i = 0; i < points.Length; i++)
            {
                UnityEngine.Gizmos.DrawLine(points[i].Position, points[i].Position + points[i].Normal);
            }
        }
    }
}