using UnityEditor;

/// <summary>
/// Ajustes de importación para la pistola y los brazos exportados con TsitskiExport
/// (Assets/Weapons/Pistol). Renombra las tomas a "Idle" / "Attack" y hace que Idle haga loop.
/// </summary>
public class PistolViewModelImport : AssetPostprocessor
{
    const string Folder = "Assets/Weapons/";

    // Subir este número hace que Unity vuelva a importar los modelos con estos ajustes
    public override uint GetVersion() => 4;

    void OnPreprocessModel()
    {
        if (!assetPath.StartsWith(Folder)) return;
        var mi = (ModelImporter)assetImporter;
        mi.animationType = ModelImporterAnimationType.Generic;
        mi.importAnimation = true;
        mi.importCameras = false;
        mi.importLights = false;
    }

    void OnPreprocessAnimation()
    {
        if (!assetPath.StartsWith(Folder)) return;
        var mi = (ModelImporter)assetImporter;
        var clips = mi.defaultClipAnimations;
        foreach (var c in clips)
        {
            // "AimAttack" antes que "Aim" y que "Attack", porque contiene las dos palabras
            if (c.takeName.Contains("Reload")) { c.name = "Reload"; c.loopTime = false; }
            else if (c.takeName.Contains("Fists Idle")) { c.name = "FistsIdle"; c.loopTime = true; }
            else if (c.takeName.Contains("Fists Punch")) { c.name = "FistsPunch"; c.loopTime = false; }
            else if (c.takeName.Contains("Draw") || c.takeName.Contains("Equip")) { c.name = "Draw"; c.loopTime = false; }
            else if (c.takeName.Contains("Holster") || c.takeName.Contains("Hide")) { c.name = "Holster"; c.loopTime = false; }
            else if (c.takeName.Contains("AimAttack")) { c.name = "AimAttack"; c.loopTime = false; }
            else if (c.takeName.Contains("Aim")) { c.name = "Aim"; c.loopTime = false; }   // sube el arma y se queda en el último fotograma
            else if (c.takeName.Contains("Idle")) { c.name = "Idle"; c.loopTime = true; }
            else if (c.takeName.Contains("Attack")) { c.name = "Attack"; c.loopTime = false; }

            // Una pose de un solo fotograma (como Idle) Unity la importa con duración 0 y sin curvas:
            // el clip queda vacío y no coloca los huesos. Con 2 fotogramas sí guarda la pose.
            if (c.lastFrame <= c.firstFrame) c.lastFrame = c.firstFrame + 1f;

            // TsitskiExport hornea la pistola desde un frame antes que los brazos (frame 1 de la pistola = frame 0 de
            // los brazos), así que la pistola iba un frame retrasada y se separaba de la mano en los movimientos
            // rápidos. Se recorta su primer frame para que los dos clips empiecen en el mismo instante.
            if (c.takeName.Contains("Pistol Arms") && c.lastFrame - c.firstFrame > 1f) c.firstFrame += 1f;
        }
        mi.clipAnimations = clips;
    }

    void OnPreprocessTexture()
    {
        if (!assetPath.StartsWith(Folder)) return;
        var ti = (TextureImporter)assetImporter;
        ti.filterMode = UnityEngine.FilterMode.Point;   // estilo PSX
        ti.mipmapEnabled = false;
        ti.textureCompression = TextureImporterCompression.Uncompressed;   // en texturas de 16-128 px la compresión emborrona los píxeles
        ti.wrapMode = UnityEngine.TextureWrapMode.Clamp;                   // que no se cuele el borde opuesto en las islas
    }
}
