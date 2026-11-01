namespace RoweRC;

internal static class TerrainSupport
{
    public const string Level = "/Game/RoweRCMaps/apex_wangan/Tandyz_Overcast_With_APEX_Repaired";
    public const string DataSha256 = "80e18b015d3fc1bd19808a1d565c085b09bcf2e83c154f644227891a83c85fe8";
    public const string Profile = "apex-grounded-camera-1:" + DataSha256;
    public static void EnsureInstalled(string gameFolder)
    {
        var file = Path.Combine(gameFolder, "RCDriftSim/Binaries/Win64/RoweRC/Terrain/apex-wangan.rrcterrain");
        if (File.Exists(file) && MapCatalog.Hash(file) == DataSha256) return;
        using var resource = typeof(TerrainSupport).Assembly.GetManifestResourceStream("RoweRC.ApexTerrain.gz")
            ?? throw new InvalidDataException("The bundled APEX terrain support is missing. Reinstall RoweRC.");
        using var compressed = new System.IO.Compression.GZipStream(resource, System.IO.Compression.CompressionMode.Decompress);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var temporary = file + ".extracting";
        try
        {
            using (var output = File.Create(temporary)) compressed.CopyTo(output);
            if (MapCatalog.Hash(temporary) != DataSha256) throw new InvalidDataException("Bundled APEX terrain checksum failed.");
            if (File.Exists(file)) File.Copy(file, file + ".previous-" + Guid.NewGuid().ToString("N"));
            File.Move(temporary, file, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public static void Verify(string gameFolder, string level)
    {
        if (level != Level) return;
        var file = Path.Combine(gameFolder, "RCDriftSim/Binaries/Win64/RoweRC/Terrain/apex-wangan.rrcterrain");
        if (!File.Exists(file) || MapCatalog.Hash(file) != DataSha256)
            throw new InvalidDataException("APEX terrain support is missing or changed. Reinstall the matching RoweRC update.");
    }
}
