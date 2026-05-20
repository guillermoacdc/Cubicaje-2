using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using Microsoft.MixedReality.OpenXR;

/// <summary>
/// Gestiona la fase de escaneo de piezas:
///   1. Escucha QRs con formato "A1;0.166;0.083;0.0415" (id;alto_m;ancho_m;fondo_m)
///   2. Muestra un holograma cian por 1 segundo por cada pieza
///   3. Guarda todas las piezas en un JSON automáticamente tras cada escaneo
///
/// Setup en Unity Editor:
///   - Asignar ARMarkerManager en el campo markerMgr
///</summary>
public class PieceScanManager : MonoBehaviour
{
    // ===== AR =====
    [Header("AR")]
    [Tooltip("ARMarkerManager configurado en la escena.")]
    public ARMarkerManager markerMgr;

    // ===== Apariencia holograma =====
    [Header("Holograma de escaneo")]
    [Tooltip("Color del holograma que aparece al escanear una pieza.")]
    public Color scanColor = new Color(0f, 0.9f, 1f, 0.75f);   // Cian semi-transparente

    [Tooltip("Tiempo que el holograma permanece visible (s).")]
    public float hologramLifetime = 1.0f;

    // ===== Filtros anti-spam =====
    [Header("Filtros QR")]
    [Tooltip("Tiempo mínimo entre el MISMO texto de QR (s).")]
    public float qrCooldown = 0.40f;

    [Tooltip("Tiempo mínimo entre CUALQUIER QR procesado (s).")]
    public float qrGlobalCooldown = 2.0f;

    [Tooltip("Segundos al inicio en que se ignoran lecturas.")]
    public float coldStartIgnore = 0.5f;

    // ===== Internos =====
    private float appStartTime;
    private string lastQR;
    private float lastQRTime = -999f;
    private float lastAnyQRTime = -999f;

    /// <summary>Piezas escaneadas, indexadas por ID (evita duplicados).</summary>
    private readonly Dictionary<string, PieceEntry> scannedPieces = new Dictionary<string, PieceEntry>();

    // ===== Ciclo de vida =====

    void Awake()
    {
        appStartTime = Time.realtimeSinceStartup;

        if (markerMgr == null)
            markerMgr = FindObjectOfType<ARMarkerManager>();

        if (markerMgr == null)
        {
            Debug.LogError("[PieceScan] No se encontró ARMarkerManager en la escena.");
            enabled = false;
            return;
        }
    }

    void OnEnable()
    {
        if (markerMgr != null)
            markerMgr.markersChanged += OnMarkersChanged;

        Debug.Log("[PieceScan] Escaneo iniciado. Apunta a los QR de las piezas.");
    }

    void OnDisable()
    {
        if (markerMgr != null)
            markerMgr.markersChanged -= OnMarkersChanged;
    }

    // ===== Eventos de marcadores =====

    private void OnMarkersChanged(ARMarkersChangedEventArgs e)
    {
        foreach (var m in e.added)   TryProcessMarker(m);
        foreach (var m in e.updated) TryProcessMarker(m);
    }

    private void TryProcessMarker(ARMarker m)
    {
        if (m == null) return;
        if (Time.realtimeSinceStartup < appStartTime + coldStartIgnore) return;
        if (m.trackingState != TrackingState.Tracking) return;

        var decoded = markerMgr.GetDecodedString(m.trackableId);
        if (string.IsNullOrEmpty(decoded)) return;

        string qr = decoded.Trim();
        float now = Time.time;

        // Cooldown global
        if (now - lastAnyQRTime < qrGlobalCooldown) return;

        // Cooldown mismo QR
        if (qr == lastQR && (now - lastQRTime) < qrCooldown) return;

        lastQR = qr;
        lastQRTime = now;
        lastAnyQRTime = now;

        // Parsear formato "A1;0.166;0.083;0.0415"
        if (!TryParsePieceQR(qr, out PieceEntry entry))
        {
            Debug.LogWarning($"[PieceScan] QR no reconocido o mal formado: '{qr}'");
            return;
        }

        // Guardar/actualizar en el diccionario
        bool isUpdate = scannedPieces.ContainsKey(entry.id);
        scannedPieces[entry.id] = entry;

        string accion = isUpdate ? "Actualizada" : "Nueva pieza";
        Debug.Log($"[PieceScan] {accion} → ID={entry.id}  alto={entry.alto_m}m  ancho={entry.ancho_m}m  fondo={entry.fondo_m}m  (Total: {scannedPieces.Count})");

        // Auto-guardar tras cada escaneo
        SaveToJson();

        // Mostrar holograma 1 segundo
        SpawnScanHologram(m, entry);
    }

    // ===== Parseo del QR =====

    /// <summary>
    /// Parsea "A1;0.166;0.083;0.0415" → PieceEntry con valores en metros.
    /// Retorna false si el formato no es válido.
    /// </summary>
    private bool TryParsePieceQR(string qr, out PieceEntry entry)
    {
        entry = null;
        var parts = qr.Split(';');

        if (parts.Length != 4)
            return false;

        string id = parts[0].Trim();
        if (string.IsNullOrEmpty(id))
            return false;

        if (!float.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float alto))
            return false;
        if (!float.TryParse(parts[2].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float ancho))
            return false;
        if (!float.TryParse(parts[3].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float fondo))
            return false;

        entry = new PieceEntry
        {
            id      = id,
            alto_m  = alto,
            ancho_m = ancho,
            fondo_m = fondo
        };
        return true;
    }

    // ===== Holograma de escaneo =====

    private void SpawnScanHologram(ARMarker marker, PieceEntry entry)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = entry.id + "_SCAN";
        go.transform.position   = marker.transform.position;
        go.transform.rotation   = marker.transform.rotation;
        // scale: (ancho=X, alto=Y, fondo=Z) — convención Unity
        go.transform.localScale = new Vector3(entry.ancho_m, entry.alto_m, entry.fondo_m);

        var shader = Shader.Find("Legacy Shaders/Transparent/Diffuse")
                     ?? Shader.Find("Legacy Shaders/Diffuse");
        var mat = new Material(shader) { color = scanColor };

        var r = go.GetComponent<Renderer>();
        if (r) r.sharedMaterial = mat;

        go.GetComponent<Collider>().enabled = false;

        StartCoroutine(DestroyAfterSeconds(go, hologramLifetime));
    }

    private IEnumerator DestroyAfterSeconds(GameObject go, float seconds)
    {
        yield return new WaitForSeconds(seconds);
        if (go != null) Destroy(go);
    }

    private void SaveToJson()
    {
        var wrapper = new PieceListWrapper();
        foreach (var kvp in scannedPieces)
            wrapper.piezas.Add(kvp.Value);

        // Ordenar por ID para que el JSON sea más legible
        wrapper.piezas.Sort((a, b) => string.Compare(a.id, b.id, System.StringComparison.Ordinal));

        string json = JsonUtility.ToJson(wrapper, prettyPrint: true);
        string path = Path.Combine(Application.persistentDataPath, "piezas.json");

        File.WriteAllText(path, json);

        Debug.Log($"[PieceScan] JSON guardado → {path}");
        Debug.Log($"[PieceScan] {wrapper.piezas.Count} pieza(s) registrada(s).");
        Debug.Log($"[PieceScan] Contenido:\n{json}");
    }
}
