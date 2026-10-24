using System.Collections.Generic;
using System.IO;
using System.Linq;
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

        /// <summary>Читает проект; проект старого формата (без участков) переносится в один участок.</summary>
        public static ProtectionProject FromJson(string json)
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
            var project = (ProtectionProject)Serializer.ReadObject(stream);

            // Десериализатор не вызывает конструкторы — восстанавливаем коллекции и строки.
            project.Title = "";
            project.Objects ??= new List<ProtectedObject>();
            project.Rods ??= new List<LightningRod>();
            project.Systems ??= new List<ProtectionSystem>();
            project.Drawing ??= new DrawingSettings();
            foreach (var system in project.Systems)
            {
                system.Objects ??= new List<ProtectedObject>();
                system.Rods ??= new List<LightningRod>();
                system.Name ??= "";
                system.Id ??= System.Guid.NewGuid().ToString("N");
            }
            foreach (var obj in project.Objects.Concat(project.Systems.SelectMany(s => s.Objects)))
            {
                obj.Contour ??= new List<Point2>();
                obj.Center ??= new Point2();
                obj.Name ??= "";
            }

            project.EnsureSystems();
            return project;
        }

        public static ProtectionProject Clone(ProtectionProject project) => FromJson(ToJson(project));
    }
}
