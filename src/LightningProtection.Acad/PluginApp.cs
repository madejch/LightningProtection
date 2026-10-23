using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Runtime;
using LightningProtection.Acad.UI;

[assembly: ExtensionApplication(typeof(LightningProtection.Acad.PluginApp))]
[assembly: CommandClass(typeof(LightningProtection.Acad.Commands))]

namespace LightningProtection.Acad
{
    public sealed class PluginApp : IExtensionApplication
    {
        /// <summary>
        /// Исключение из Initialize AutoCAD считает ошибкой загрузки и не регистрирует команды,
        /// поэтому шрифт и лента подключаются с перехватом ошибок.
        /// </summary>
        public void Initialize()
        {
            var ed = Application.DocumentManager.MdiActiveDocument?.Editor;
            ed?.WriteMessage("\nМолниезащита загружена: вкладка «Молниезащита» на ленте, команды LPROT, LPROTDRAW, LPROTREPORT, LPROTCLEAR.\n");

            try
            {
                var fontMessage = FontInstaller.EnsureInstalled();
                if (fontMessage != null)
                    ed?.WriteMessage(fontMessage + "\n");
            }
            catch (System.Exception ex)
            {
                ed?.WriteMessage($"Шрифт {FontInstaller.FontFace} не установлен: {ex.Message}\n");
            }

            try
            {
                RibbonBuilder.Initialize();
            }
            catch (System.Exception ex)
            {
                ed?.WriteMessage($"Вкладка на ленте не создана: {ex.Message}\n");
            }
        }

        public void Terminate()
        {
        }
    }
}
