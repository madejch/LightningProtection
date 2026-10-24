using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.GraphicsInterface;

namespace LightningProtection.Acad.Drawing
{
    /// <summary>
    /// Предпросмотр вставки: уже построенные (ещё не сохранённые) объекты рисуются «призраком»
    /// со смещением от базовой точки к курсору, вокруг — рамка их габаритов.
    /// </summary>
    internal sealed class PlacementJig : DrawJig
    {
        public const string SeparateKeyword = "Separate";

        private readonly IReadOnlyList<Entity> _entities;
        private readonly Point3d _basePoint;
        private readonly Extents3d _extents;
        private readonly string _message;
        private readonly bool _offerSeparate;
        private Point3d _current;

        public PlacementJig(IReadOnlyList<Entity> entities, Point3d basePoint, Extents3d extents, string message, bool offerSeparate)
        {
            _entities = entities;
            _basePoint = basePoint;
            _extents = extents;
            _message = message;
            _offerSeparate = offerSeparate;
            _current = basePoint;
        }

        /// <summary>Смещение от базовой точки к указанной.</summary>
        public Vector3d Displacement => _current - _basePoint;

        protected override SamplerStatus Sampler(JigPrompts prompts)
        {
            var options = new JigPromptPointOptions("\n" + _message)
            {
                UserInputControls = UserInputControls.Accept3dCoordinates | UserInputControls.NullResponseAccepted,
            };
            if (_offerSeparate)
            {
                options.Keywords.Add(SeparateKeyword, "Отдельно", "Отдельно");
                options.AppendKeywordsToMessage = true;
            }

            var result = prompts.AcquirePoint(options);
            if (result.Status == PromptStatus.Keyword)
                return SamplerStatus.OK;
            if (result.Status != PromptStatus.OK)
                return SamplerStatus.Cancel;
            if (result.Value.DistanceTo(_current) < Tolerance.Global.EqualPoint)
                return SamplerStatus.NoChange;
            _current = result.Value;
            return SamplerStatus.OK;
        }

        protected override bool WorldDraw(WorldDraw draw)
        {
            var geometry = draw.Geometry;
            geometry.PushModelTransform(Matrix3d.Displacement(Displacement));
            foreach (var entity in _entities)
                geometry.Draw(entity);

            using (var frame = new Autodesk.AutoCAD.DatabaseServices.Polyline())
            {
                frame.AddVertexAt(0, new Point2d(_extents.MinPoint.X, _extents.MinPoint.Y), 0, 0, 0);
                frame.AddVertexAt(1, new Point2d(_extents.MaxPoint.X, _extents.MinPoint.Y), 0, 0, 0);
                frame.AddVertexAt(2, new Point2d(_extents.MaxPoint.X, _extents.MaxPoint.Y), 0, 0, 0);
                frame.AddVertexAt(3, new Point2d(_extents.MinPoint.X, _extents.MaxPoint.Y), 0, 0, 0);
                frame.Closed = true;
                frame.ColorIndex = 2;
                geometry.Draw(frame);
            }
            geometry.PopModelTransform();
            return true;
        }
    }
}
