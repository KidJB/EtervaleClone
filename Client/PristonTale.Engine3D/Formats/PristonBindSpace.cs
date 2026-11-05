using Microsoft.Xna.Framework;

namespace PristonTale.Engine3D.Formats;

// The skinning takes each skinned vertex in its bone's space, as the older models store it. The float model layouts
// (0.65 and 0.66) store skinned vertices in the bind pose instead, so they are carried into their bone's space with the
// inverse of the bone's bind transform once the skeleton is known.
public static class PristonBindSpace
{
    public static bool StoresBindPose(PristonModel mesh) =>
        mesh.Header?.Kind is PristonSmdHeaderKind.Version65Model or PristonSmdHeaderKind.Version66Model;

    public static PristonModel ToBoneSpace(PristonModel mesh, PristonModel skeleton)
    {
        if (!StoresBindPose(mesh))
        {
            return mesh;
        }

        var inverses = new Dictionary<string, Matrix>(StringComparer.OrdinalIgnoreCase);
        foreach (var bone in skeleton.Objects)
        {
            if (bone.Name.Length > 0 && !inverses.ContainsKey(bone.Name))
            {
                inverses[bone.Name] = Matrix.Invert(bone.Transform);
            }
        }

        var objects = new List<PristonModelObject>(mesh.Objects.Count);
        foreach (var obj in mesh.Objects)
        {
            objects.Add(obj.HasPhysique && obj.PhysiqueBoneNames.Count == obj.Vertices.Count ? ObjectToBoneSpace(obj, inverses) : obj);
        }

        return mesh with { Objects = objects };
    }

    private static PristonModelObject ObjectToBoneSpace(PristonModelObject obj, Dictionary<string, Matrix> inverses)
    {
        var vertices = new List<PristonModelVertex>(obj.Vertices.Count);
        for (var index = 0; index < obj.Vertices.Count; index++)
        {
            var vertex = obj.Vertices[index];
            if (!inverses.TryGetValue(obj.PhysiqueBoneNames[index], out var inverse))
            {
                vertices.Add(vertex);
                continue;
            }

            var normal = Vector3.TransformNormal(vertex.Normal, inverse);
            if (normal.LengthSquared() > 0.000001f)
            {
                normal.Normalize();
            }

            vertices.Add(new PristonModelVertex(Vector3.Transform(vertex.Position, inverse), normal));
        }

        return obj with { Vertices = vertices };
    }
}
