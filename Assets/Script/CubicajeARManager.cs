using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using Microsoft.MixedReality.OpenXR;

public class CubicajeARManager : MonoBehaviour
{
    // ===== AR / Managers =====
    [Header("AR")]
    [Tooltip("Asigna aquí el ARMarkerManager que ya tienes configurado en la escena.")]
    public ARMarkerManager markerMgr;

    // ===== Apariencia pallet =====
    [Header("Apariencia pallet")]
    public Color palletWallColor = new Color(0.58f, 0.85f, 1f, 0.35f);
    public Color palletFloorColor = new Color(0.50f, 0.80f, 1f, 0.50f);
    public Color palletBackColorIdle = new Color(0.0f, 0.5f, 0.15f, 0.75f);
    public Color palletBackStrongGreen = new Color(0.0f, 0.85f, 0.15f, 0.95f);

    // ===== Hologramas =====
    [Header("Hologramas en vuelo / error (100% alpha)")]
    public Color travelColor = new Color(0.1f, 1f, 0.1f, 1f);   // Verde opaco
    public Color errorColor = new Color(1f, 0f, 0f, 1f);       // Rojo opaco

    // ===== Geometría PALLET =====
    [Header("Pallet")]
    // 21 x 21 x 21 cm → 0.21 m
    public Vector3 palletOuterDims = new Vector3(0.21f, 0.21f, 0.21f);
    public float wallThickness = 0.005f;
    public float baseHeightNudge = 0.0f;   // 0 → apoyado justo encima del QR

    [Header("Tiempos / filtros")]
    public float coldStartIgnoreSeconds = 0.5f;  // ignorar lecturas muy al inicio

    [Tooltip("Filtro anti-spam para el MISMO texto de QR (s).")]
    public float qrCooldown = 0.40f;

    [Tooltip("Tiempo mínimo entre lecturas/procesos de CUALQUIER QR (s).")]
    public float qrGlobalCooldownSeconds = 2.0f;

    public float errorLifetimeSeconds = 1.0f;  // duración holograma rojo

    // ===== Animaciones =====
    [Header("Animación pallet")]
    public float palletPopDuration = 0.25f;

    [Header("Movimiento de piezas")]
    [Tooltip("Velocidad constante del trayecto (m/s).")]
    public float pieceTravelSpeed = 1.0f;

    [Tooltip("Desplazamiento del punto de entrada por delante del pallet (m).")]
    public float entryFrontOffset = 0.07f;

    [Tooltip("Margen interior para no tocar paredes (m).")]
    public float innerMargin = 0.008f;

    [Tooltip("Factor de caída vertical de la curva (proporcional a la distancia).")]
    public float curveDropFactor = 0.15f;

    [Tooltip("Resolución de muestreo por cada segmento de la curva.")]
    public int curveSamplesPerSegment = 30;

    // ===== Internos AR / escena =====
    private ARSession arSession;
    private Camera cam;

    private GameObject pallet;      // raíz del pallet hueco
    private Transform palletFloor; // piso
    private Bounds palletInnerBounds;
    private Renderer palletBackRenderer;

    private float appStartTime;

    private enum State { WaitingPallet, PalletLocked, Done }
    private State state = State.WaitingPallet;

    private int seqIndex = 0;

    // Control lecturas
    private string lastQR;
    private float lastQRTime = -999f;
    private float lastAnyQRProcessTime = -999f;   // << NUEVO: tiempo de último QR procesado (global)

    // Holograma en vuelo (persistente hasta el siguiente escaneo correcto)
    private GameObject currentTravelPiece;
    private Coroutine currentTravelRoutine;

    // Holograma de error temporal
    private GameObject currentErrorPiece;

    // ===== Piezas (calculadas dinámicamente por CubicajeEngine) =====

    /// <summary>Piezas colocadas por el algoritmo de cubicaje, en secuencia de ensamblado.</summary>
    private List<PlacedPiece> placedPieces = new List<PlacedPiece>();

    /// <summary>Secuencia de IDs en el orden de ensamblado calculado.</summary>
    private string[] assemblySequence = new string[0];

    // ===== Ciclo de vida =====
    void Awake()
    {
        arSession = FindObjectOfType<ARSession>();
        cam = Camera.main;
        appStartTime = Time.realtimeSinceStartup;

        if (cam != null)
        {
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0, 0, 0, 0);
        }
        RenderSettings.skybox = null;

        if (markerMgr == null)
        {
            markerMgr = FindObjectOfType<ARMarkerManager>();
            if (markerMgr == null)
            {
                Debug.LogError("[CubicajeAR] No se encontró ningún ARMarkerManager en la escena.");
                enabled = false;
                return;
            }
        }

        state = State.WaitingPallet;
        seqIndex = 0;

        markerMgr.enabled = true;

        Debug.Log($"[CubicajeAR] Awake OK — markerMgr={(markerMgr != null ? markerMgr.name : "NULL")}");
    }

    /// <summary>
    /// Lee piezas.json desde el almacenamiento persistente, llama al motor de cubicaje
    /// y construye la secuencia de ensamblado. Retorna false si algo falla.
    /// </summary>
    private bool LoadAndSolve()
    {
        string path = Path.Combine(Application.persistentDataPath, "piezas.json");

        if (!File.Exists(path))
        {
            Debug.LogError($"[CubicajeAR] piezas.json no encontrado en: {path}");
            return false;
        }

        string json = File.ReadAllText(path);
        var wrapper = JsonUtility.FromJson<PieceListWrapper>(json);

        if (wrapper == null || wrapper.piezas == null || wrapper.piezas.Count == 0)
        {
            Debug.LogError("[CubicajeAR] piezas.json está vacío o mal formado.");
            return false;
        }

        // Dimensiones interiores del pallet (igual a las que calcula CreateHollowPallet)
        var palletInner = new Vector3(
            palletOuterDims.x - 2f * wallThickness,
            palletOuterDims.y - wallThickness,
            palletOuterDims.z - wallThickness
        );

        placedPieces  = CubicajeEngine.Solve(wrapper.piezas, palletInner, innerMargin);
        assemblySequence = new string[placedPieces.Count];
        for (int i = 0; i < placedPieces.Count; i++)
            assemblySequence[i] = placedPieces[i].id;

        Debug.Log($"[CubicajeAR] Secuencia calculada ({assemblySequence.Length} piezas): " +
                  string.Join(" → ", assemblySequence));
        return true;
    }

    void OnEnable()
    {
        Debug.Log($"[CubicajeAR] OnEnable — markerMgr={(markerMgr != null ? "OK" : "NULL → NO SE SUSCRIBE")}");
        if (markerMgr != null)
            markerMgr.markersChanged += OnMarkersChanged;
    }

    void OnDisable()
    {
        if (markerMgr != null)
            markerMgr.markersChanged -= OnMarkersChanged;
    }

    // ===== Eventos de marcadores =====
    private void OnMarkersChanged(ARMarkersChangedEventArgs e)
    {
        if (state == State.Done)
            return;

        foreach (var m in e.added) TryProcessMarker(m);
        foreach (var m in e.updated) TryProcessMarker(m);
    }

    private void TryProcessMarker(ARMarker m)
    {
        if (m == null) return;

        var _decoded = markerMgr.GetDecodedString(m.trackableId);
        Debug.Log($"[CubicajeAR] TryProcessMarker — qr='{_decoded}' tracking={m.trackingState} state={state}");

        if (Time.realtimeSinceStartup < appStartTime + coldStartIgnoreSeconds) return;
        if (m.trackingState != TrackingState.Tracking) return;

        var decoded = markerMgr.GetDecodedString(m.trackableId);
        if (string.IsNullOrEmpty(decoded)) return;

        string qr = decoded.Trim().ToUpperInvariant();
        float now = Time.time;

        // --- 1) Cooldown GLOBAL: no procesar ningún QR hasta que pase el tiempo configurado ---
        if (now - lastAnyQRProcessTime < qrGlobalCooldownSeconds)
        {
            return;
        }

        // --- 2) Cooldown por MISMO TEXTO para evitar duplicados pegados ---
        if (qr == lastQR && (now - lastQRTime) < qrCooldown)
        {
            return;
        }

        // A partir de aquí, se acepta este QR como "procesado"
        lastQR = qr;
        lastQRTime = now;
        lastAnyQRProcessTime = now;

        // Esperando pallet
        if (state == State.WaitingPallet)
        {
            if (qr == "PALLET" || qr == "PALLET.")
                ProcessPalletMarker(m);
            return;
        }

        // Después de fijar el pallet
        if (state != State.PalletLocked) return;

        if (qr == "PALLET" || qr == "PALLET.")
            return; // ignorar pallet una vez fijado

        if (!qr.StartsWith("A") && !qr.StartsWith("B"))
            return;

        if (seqIndex >= assemblySequence.Length) return;

        string expectedId = assemblySequence[seqIndex];

        if (qr != expectedId)
        {
            // incorrecta → holograma rojo
            var wrongDef = FindPlacedById(qr);
            SpawnErrorAtMarker(m, wrongDef);
            return;
        }

        // correcta → holograma verde que viaja curvado hacia el pallet
        var def = FindPlacedById(expectedId);
        HandleCorrectPiece(m, def);
    }

    // ===== Pallet =====
    private void ProcessPalletMarker(ARMarker m)
    {
        if (pallet != null) return;

        // Leer piezas.json y calcular la secuencia de ensamblado en este momento,
        // cuando ya se escanearon todas las piezas en la misma sesión.
        if (!LoadAndSolve()) return;

        var qrPos = m.transform.position;
        var qrYaw = Quaternion.Euler(0f, m.transform.rotation.eulerAngles.y, 0f);

        pallet = CreateHollowPallet("PALLET", palletOuterDims, wallThickness,
                                    palletWallColor, palletFloorColor,
                                    palletBackColorIdle,
                                    out palletFloor, out palletInnerBounds, out palletBackRenderer);

        var centerAboveQR = qrPos + Vector3.up * (palletOuterDims.y * 0.5f + baseHeightNudge);
        pallet.transform.SetPositionAndRotation(centerAboveQR, qrYaw);

        // frente del pallet hacia la cámara
        if (cam != null)
        {
            Vector3 toCam = (cam.transform.position - pallet.transform.position);
            toCam.y = 0f;
            if (toCam.sqrMagnitude > 0.0001f)
            {
                var faceCam = Quaternion.LookRotation(toCam.normalized, Vector3.up);
                faceCam *= Quaternion.Euler(0f, 180f, 0f);
                pallet.transform.rotation = faceCam;
            }
        }

        if (pallet.GetComponent<ARAnchor>() == null)
            pallet.AddComponent<ARAnchor>();

        StartCoroutine(AnimatePalletIn());

        state = State.PalletLocked;
    }

    private GameObject CreateHollowPallet(
        string name, Vector3 outer, float t,
        Color wallCol, Color floorCol, Color backCol,
        out Transform floor, out Bounds inner, out Renderer backRenderer)
    {
        GameObject root = new GameObject(name);

        var shader = Shader.Find("Legacy Shaders/Transparent/Diffuse")
                     ?? Shader.Find("Legacy Shaders/Diffuse");

        var matWall = new Material(shader) { color = wallCol };
        var matFloor = new Material(shader) { color = floorCol };
        var matBack = new Material(shader) { color = backCol };

        // Piso
        var baseGO = GameObject.CreatePrimitive(PrimitiveType.Cube);
        baseGO.name = "Floor";
        baseGO.transform.SetParent(root.transform, false);
        baseGO.transform.localScale = new Vector3(outer.x, t, outer.z);
        baseGO.transform.localPosition = new Vector3(0, -outer.y * 0.5f + t * 0.5f, 0);
        baseGO.GetComponent<Renderer>().sharedMaterial = matFloor;
        baseGO.GetComponent<Collider>().enabled = false;

        // Cara trasera
        var back = GameObject.CreatePrimitive(PrimitiveType.Cube);
        back.name = "Back";
        back.transform.SetParent(root.transform, false);
        back.transform.localScale = new Vector3(outer.x, outer.y, t);
        back.transform.localPosition = new Vector3(0, 0, outer.z * 0.5f - t * 0.5f);
        var backR = back.GetComponent<Renderer>();
        backR.sharedMaterial = matBack;
        back.GetComponent<Collider>().enabled = false;

        // Lados
        var left = GameObject.CreatePrimitive(PrimitiveType.Cube);
        left.name = "Left";
        left.transform.SetParent(root.transform, false);
        left.transform.localScale = new Vector3(t, outer.y, outer.z);
        left.transform.localPosition = new Vector3(-outer.x * 0.5f + t * 0.5f, 0, 0);
        left.GetComponent<Renderer>().sharedMaterial = matWall;
        left.GetComponent<Collider>().enabled = false;

        var right = GameObject.CreatePrimitive(PrimitiveType.Cube);
        right.name = "Right";
        right.transform.SetParent(root.transform, false);
        right.transform.localScale = new Vector3(t, outer.y, outer.z);
        right.transform.localPosition = new Vector3(outer.x * 0.5f - t * 0.5f, 0, 0);
        right.GetComponent<Renderer>().sharedMaterial = matWall;
        right.GetComponent<Collider>().enabled = false;

        floor = baseGO.transform;
        inner = new Bounds(Vector3.zero, new Vector3(outer.x - 2 * t, outer.y - t, outer.z - t));
        backRenderer = backR;
        return root;
    }

    private IEnumerator AnimatePalletIn()
    {
        if (pallet == null) yield break;

        float duration = palletPopDuration;
        float t = 0f;
        Vector3 start = Vector3.zero;
        Vector3 end = Vector3.one;

        pallet.transform.localScale = start;

        while (t < duration)
        {
            t += Time.deltaTime;
            float u = Mathf.Clamp01(t / duration);
            u = u * u * (3f - 2f * u);
            pallet.transform.localScale = Vector3.Lerp(start, end, u);
            yield return null;
        }

        pallet.transform.localScale = end;
    }

    // ===== Piezas =====

    /// <summary>
    /// Busca una pieza en la lista calculada por el cubicaje.
    /// Si no se encuentra (pieza desconocida), retorna un PlacedPiece con escala mínima.
    /// </summary>
    private PlacedPiece FindPlacedById(string id)
    {
        foreach (var p in placedPieces)
            if (p.id == id) return p;

        // Pieza no encontrada en el plan: usar tamaño genérico para mostrar error
        return new PlacedPiece
        {
            id         = id,
            localPos   = Vector3.zero,
            localEuler = Vector3.zero,
            scale      = new Vector3(0.1f, 0.1f, 0.1f)
        };
    }

    private void HandleCorrectPiece(ARMarker marker, PlacedPiece def)
    {
        // Eliminar holograma de viaje anterior si existía
        if (currentTravelRoutine != null)
        {
            StopCoroutine(currentTravelRoutine);
            currentTravelRoutine = null;
        }
        if (currentTravelPiece != null)
        {
            Destroy(currentTravelPiece);
            currentTravelPiece = null;
        }

        // Posición de inicio: donde está el QR físico
        Vector3 startPos = marker.transform.position;
        Quaternion startRot = marker.transform.rotation;

        // Posición de destino: slot calculado por el cubicaje, en espacio mundo
        Vector3 endPos = pallet.transform.TransformPoint(def.localPos);
        Quaternion endRot = pallet.transform.rotation * Quaternion.Euler(def.localEuler);

        currentTravelPiece = CreateTravelPiece(def, startPos, startRot);

        var path = BuildCurvedEntryPath(startPos, endPos, def);

        currentTravelRoutine = StartCoroutine(MoveAlongPath(
            currentTravelPiece,
            path,
            pieceTravelSpeed,
            endRot,
            () =>
            {
                seqIndex++;
                if (seqIndex >= assemblySequence.Length)
                {
                    state = State.Done;
                    StartCoroutine(PalletCompletedFeedback());
                    markerMgr.enabled = false;
                }
                // si aún faltan piezas, el holograma se queda en su slot
            }
        ));
    }

    private GameObject CreateTravelPiece(PlacedPiece d, Vector3 worldPos, Quaternion worldRot)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = d.id + "_TRAVEL";
        go.transform.position   = worldPos;
        go.transform.rotation   = worldRot;
        go.transform.localScale = d.scale;

        var shader = Shader.Find("Legacy Shaders/Transparent/Diffuse")
                     ?? Shader.Find("Legacy Shaders/Diffuse");
        var mat = new Material(shader) { color = travelColor };

        var r = go.GetComponent<Renderer>();
        if (r) r.sharedMaterial = mat;
        go.GetComponent<Collider>().enabled = false;

        return go;
    }

    private void SpawnErrorAtMarker(ARMarker marker, PlacedPiece def)
    {
        if (currentErrorPiece != null)
        {
            Destroy(currentErrorPiece);
            currentErrorPiece = null;
        }

        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = def.id + "_ERROR";
        go.transform.position   = marker.transform.position;
        go.transform.rotation   = marker.transform.rotation;
        go.transform.localScale = def.scale;

        var shader = Shader.Find("Legacy Shaders/Transparent/Diffuse")
                     ?? Shader.Find("Legacy Shaders/Diffuse");
        var mat = new Material(shader) { color = errorColor };

        var r = go.GetComponent<Renderer>();
        if (r) r.sharedMaterial = mat;
        go.GetComponent<Collider>().enabled = false;

        currentErrorPiece = go;

        StartCoroutine(DestroyAfterSeconds(go, errorLifetimeSeconds));
    }

    private IEnumerator DestroyAfterSeconds(GameObject go, float seconds)
    {
        yield return new WaitForSeconds(seconds);
        if (go != null) Destroy(go);
        if (go == currentErrorPiece) currentErrorPiece = null;
    }

    private IEnumerator PalletCompletedFeedback()
    {
        if (palletBackRenderer == null) yield break;

        var mat = palletBackRenderer.material;
        Color ini = mat.color;
        Color fin = palletBackStrongGreen;
        float t = 0f;
        float dur = 0.35f;

        while (t < dur)
        {
            t += Time.deltaTime;
            float u = Mathf.Clamp01(t / dur);
            mat.color = Color.Lerp(ini, fin, u);
            yield return null;
        }

        mat.color = fin;
    }

    // ===== Trayectoria curvada hacia abajo y entrada frontal =====
    private List<Vector3> BuildCurvedEntryPath(Vector3 startPos, Vector3 endPos, PlacedPiece def)
    {
        var points = new List<Vector3>();

        // Punto de entrada por delante del pallet (z negativo en local, frontal abierto)
        float frontZ = -palletOuterDims.z * 0.5f - entryFrontOffset;

        // Mantener altura parecida a la del destino pero dentro de los márgenes
        float yLocal = Mathf.Clamp(def.localPos.y,
            -palletInnerBounds.extents.y + innerMargin,
            palletInnerBounds.extents.y - innerMargin);

        float xLocal = Mathf.Clamp(def.localPos.x,
            -palletInnerBounds.extents.x + innerMargin,
            palletInnerBounds.extents.x - innerMargin);

        Vector3 entryLocal = new Vector3(xLocal, yLocal, frontZ);
        Vector3 entryWorld = pallet.transform.TransformPoint(entryLocal);

        // Primer segmento: start → entry, curvado hacia abajo
        float d1 = Vector3.Distance(startPos, entryWorld);
        Vector3 mid1 = (startPos + entryWorld) * 0.5f;
        Vector3 ctrl1 = mid1 + Vector3.down * (d1 * curveDropFactor);

        var seg1 = SampleQuadraticBezier(startPos, ctrl1, entryWorld, curveSamplesPerSegment);

        // Segundo segmento: entry → end, también curvado hacia abajo
        float d2 = Vector3.Distance(entryWorld, endPos);
        Vector3 mid2 = (entryWorld + endPos) * 0.5f;
        Vector3 ctrl2 = mid2 + Vector3.down * (d2 * curveDropFactor * 0.7f);

        var seg2 = SampleQuadraticBezier(entryWorld, ctrl2, endPos, curveSamplesPerSegment);

        points.AddRange(seg1);
        for (int i = 1; i < seg2.Count; i++) points.Add(seg2[i]);

        return points;
    }

    private List<Vector3> SampleQuadraticBezier(Vector3 p0, Vector3 p1, Vector3 p2, int samples)
    {
        var list = new List<Vector3>(samples + 1);
        for (int i = 0; i <= samples; i++)
        {
            float t = i / (float)samples;
            float u = 1f - t;
            Vector3 p = (u * u) * p0 + 2f * u * t * p1 + (t * t) * p2;
            list.Add(p);
        }
        return list;
    }

    private IEnumerator MoveAlongPath(
        GameObject go,
        List<Vector3> path,
        float speed,
        Quaternion endRot,
        Action onArrive)
    {
        if (go == null || path == null || path.Count < 2) yield break;

        float totalLen = 0f;
        for (int i = 1; i < path.Count; i++)
            totalLen += Vector3.Distance(path[i - 1], path[i]);

        if (totalLen < 0.001f)
        {
            go.transform.position = path[path.Count - 1];
            go.transform.rotation = endRot;
            onArrive?.Invoke();
            yield break;
        }

        float traveled = 0f;

        while (traveled < totalLen && go != null)
        {
            float step = speed * Time.deltaTime;
            traveled += step;
            float d = Mathf.Clamp(traveled, 0f, totalLen);

            // Buscar el segmento donde cae la distancia d
            float acc = 0f;
            for (int i = 1; i < path.Count; i++)
            {
                float dl = Vector3.Distance(path[i - 1], path[i]);
                if (acc + dl >= d)
                {
                    float t = Mathf.Clamp01((d - acc) / dl);
                    Vector3 pos = Vector3.Lerp(path[i - 1], path[i], t);
                    go.transform.position = pos;

                    float u = d / totalLen;
                    go.transform.rotation = Quaternion.Slerp(go.transform.rotation, endRot, Mathf.Clamp01(u));

                    break;
                }
                acc += dl;
            }

            yield return null;
        }

        if (go != null)
        {
            go.transform.position = path[path.Count - 1];
            go.transform.rotation = endRot;
        }

        onArrive?.Invoke();
        currentTravelRoutine = null;
    }
}
