using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.AutoCAD.DatabaseServices;
using LightningProtection.Core.Model;

namespace LightningProtection.Acad
{
    /// <summary>Хранит проект молниезащиты в словаре чертежа, чтобы его можно было открыть и пересчитать позже.</summary>
    internal static class ProjectStore
    {
        private const string DictionaryKey = "LPROT_PROJECT";
        private const int ChunkSize = 250;

        public static ProtectionProject Load(Database db)
        {
            using var tr = db.TransactionManager.StartTransaction();
            var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
            if (!nod.Contains(DictionaryKey))
                return CreateDefault(db);

            var record = (Xrecord)tr.GetObject(nod.GetAt(DictionaryKey), OpenMode.ForRead);
            var json = new StringBuilder();
            if (record.Data != null)
                foreach (TypedValue value in record.Data)
                    json.Append(value.Value as string);
            tr.Commit();

            try
            {
                return ProjectSerializer.FromJson(json.ToString());
            }
            catch (System.Runtime.Serialization.SerializationException)
            {
                return CreateDefault(db);
            }
        }

        public static void Save(Database db, ProtectionProject project)
        {
            var json = ProjectSerializer.ToJson(project);
            var chunks = new List<TypedValue>();
            for (var i = 0; i < json.Length; i += ChunkSize)
                chunks.Add(new TypedValue((int)DxfCode.Text, json.Substring(i, System.Math.Min(ChunkSize, json.Length - i))));

            using var tr = db.TransactionManager.StartTransaction();
            var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForWrite);
            Xrecord record;
            if (nod.Contains(DictionaryKey))
            {
                record = (Xrecord)tr.GetObject(nod.GetAt(DictionaryKey), OpenMode.ForWrite);
            }
            else
            {
                record = new Xrecord();
                nod.SetAt(DictionaryKey, record);
                tr.AddNewlyCreatedDBObject(record, true);
            }
            record.Data = new ResultBuffer(chunks.ToArray());
            tr.Commit();
        }

        private static ProtectionProject CreateDefault(Database db)
        {
            var project = new ProtectionProject();
            project.Drawing.UnitsPerMeter = db.Insunits switch
            {
                UnitsValue.Meters => 1,
                UnitsValue.Centimeters => 100,
                _ => 1000,
            };
            return project;
        }
    }
}
