namespace CheatEngine.SDK.SourceGenerators.LuaBridgeContract.Tests.Infrastructure;

/// <summary>Compact valid catalogue fixtures; full provenance is intentionally irrelevant to this managed projection.</summary>
internal static class CatalogSources
{
    public static readonly string ReverseOpcodeOrder = """
                                                       {
                                                         "schemaVersion": 1,
                                                         "catalogId": "cheatengine-sdk-lua-protected-operations",
                                                         "bridgeContract": {
                                                           "abiMajor": 1,
                                                           "minimumAbiMinor": 1,
                                                           "operationBitmapWidth": 64,
                                                           "operationBitmap": "0x0000000000000401"
                                                         },
                                                         "operations": [
                                                           {
                                                             "id": "PushHostObject",
                                                             "managed": { "constant": "PushHostObjectOperation", "wrapper": "PushHostObject" },
                                                             "opcode": 10,
                                                             "protected": true,
                                                             "requiresNativeProtection": true
                                                           },
                                                           {
                                                             "id": "PushBytes",
                                                             "managed": { "constant": "PushBytesOperation", "wrapper": "PushBytes" },
                                                             "opcode": 0,
                                                             "protected": true,
                                                             "requiresNativeProtection": true
                                                           }
                                                         ]
                                                       }
                                                       """;
}
