namespace ForbiddenEden.EditorTools
{
    /// <summary>Steps that follow a parts export, in one editor launch.</summary>
    public static class Hs2Pipeline
    {
        public static void AfterParts()
        {
            Hs2CharacterExporter.PruneCatalog();
            Hs2ShapeExporter.Export();
            Hs2CharacterExporter.ExportAdvGestures();
        }

        /// <summary>Re-exports the accessory slots with translucent parts, then previews.</summary>
        public static void TranslucentAccessories()
        {
            System.Reflection.MethodInfo m = typeof(Hs2CharacterExporter).GetMethod("ExportPartsFor", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            m.Invoke(null, new object[] { new[] { "acc_glasses", "acc_head", "acc_face" }, int.MaxValue });
            Hs2Preview.RenderAvatars();
        }

        public static void AfterPartsWithPreview()
        {
            AfterParts();
            Hs2Preview.RenderAvatars();
        }
    }
}
