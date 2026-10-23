using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;

namespace LightningProtection.Core.Model
{
    public static class ProjectSerializer
    {
        private static readonly DataContractJsonSerializer Serializer = new DataContractJsonSerializer(typeof(ProtectionProject));

        public static string ToJson(ProtectionProject project)
        {
            using var stream = new MemoryStream();
            Serializer.WriteObject(stream, project);
            return Encoding.UTF8.GetString(stream.ToArray());
        }

        public static ProtectionProject FromJson(string json)
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
            var project = (ProtectionProject)Serializer.ReadObject(stream);
            // Десериализатор не вызывает конструктор — восстанавливаем коллекции.
            project.Objects ??= new System.Collections.Generic.List<ProtectedObject>();
            project.Rods ??= new System.Collections.Generic.List<LightningRod>();
            project.Drawing ??= new DrawingSettings();
            foreach (var obj in project.Objects)
            {
                obj.Contour ??= new System.Collections.Generic.List<Point2>();
                obj.Center ??= new Point2();
                obj.Name ??= "";
            }
            return project;
        }

        public static ProtectionProject Clone(ProtectionProject project) => FromJson(ToJson(project));
    }
}
