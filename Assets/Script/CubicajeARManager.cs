using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using Microsoft.MixedReality.OpenXR;

public class CubicajeARManager : MonoBehaviour
{
    // ===== AR / Managers =====
    [Header("AR")]
    [Tooltip("ARMarkerManager configurado en la escena.")]
    public ARMarkerManager markerMgr;

    // ===== Apariencia del pallet =====
    [Header("Apariencia del pallet")]
    public Color palletWallColor = new Color(0.58f, 0.85f, 1f, 0.35f);
    public Color palletFloorColor = new Color(0.50f, 0.80f, 1f, 0.50f);
    public Color palletBackColorIdle = new Color(0.0f, 0.5f, 0.15f, 0.75f);
    public Color palletBackStrongGreen = new Color(0.0f, 0.85f, 0.15f, 0.95f);

    // ===== Hologramas =====
    [Header("Hologramas")]
    public Color travelColor = new Color(0.1f, 1f, 0.1f, 1f);
    public Color errorColor = new Color(1f, 0f, 0f, 1f);

    // ===== Geometría del pallet =====
    [Header("Pallet")]
    [Tooltip("Dimensiones exteriores del pallet virtual en metros.")]
    public Vector3 palletOuterDims = new Vector3(0.21f, 0.21f, 0.21f);

    [Tooltip("Espesor de las paredes del pallet virtual en metros.")]
    public float wallThickness = 0.005f;

    [Tooltip("Ajuste vertical adicional sobre el marcador PALLET.")]
    public float baseHeightNudge = 0.0f;

    // ===== Filtros de lectura QR =====
    [Header("Tiempos y filtros")]
    [Tooltip("Tiempo inicial durante el cual se ignoran lecturas QR.")]
    public float coldStartIgnoreSeconds = 0.5f;

    [Tooltip("Tiempo mínimo para volver a procesar el mismo texto QR.")]
    public float qrCooldown = 0.40f;

    [Tooltip("Tiempo mínimo entre el procesamiento de dos QR cualesquiera.")]
    public float qrGlobalCooldownSeconds = 2.0f;

    [Tooltip("Duración del holograma rojo de error.")]
    public float errorLifetimeSeconds = 1.0f;

    // ===== Animaciones =====
    [Header("Animación del pallet")]
    public float palletPopDuration = 0.25f;

    [Header("Movimiento de piezas")]
    [Tooltip("Velocidad del holograma durante el trayecto, en metros por segundo.")]
    public float pieceTravelSpeed = 1.0f;

    [Tooltip("Distancia del punto de entrada frente al pallet.")]
    public float entryFrontOffset = 0.07f;

    [Tooltip("Margen interior para evitar intersecciones con las paredes.")]
    public float innerMargin = 0.008f;

    [Tooltip("Caída vertical de la curva, proporcional a la distancia.")]
    public float curveDropFactor = 0.15f;

    [Tooltip("Cantidad de muestras por segmento de la trayectoria.")]
    public int curveSamplesPerSegment = 30;

    // ===== Estado interno =====
    private Camera mainCamera;
    private GameObject pallet;
    private Transform palletFloor;
    private Bounds palletInnerBounds;
    private Renderer palletBackRenderer;

    private float appStartTime;

    private enum State
    {
        WaitingPallet,
        PalletLocked,
        Done
    }

    private State state = State.WaitingPallet;
    private int sequenceIndex;

    private string lastQrText;
    private float lastQrTime = -999f;
    private float lastAnyQrProcessTime = -999f;

    private GameObject currentTravelPiece;
    private Coroutine currentTravelRoutine;
    private GameObject currentErrorPiece;

    // ===== Piezas predefinidas =====
    private struct PieceDef
    {
        public string id;
        public Vector3 localPosition;
        public Vector3 localEuler;
        public Vector3 scale;
    }

    private static readonly PieceDef[] PieceDefinitions =
    {
        new PieceDef
        {
            id = "A1",
            localPosition = new Vector3(-0.022f, -0.0635f, 0.08425f),
            localEuler = Vector3.zero,
            scale = new Vector3(0.166f, 0.083f, 0.0415f)
        },
        new PieceDef
        {
            id = "A2",
            localPosition = new Vector3(0.0635f, -0.08425f, -0.022f),
            localEuler = new Vector3(90f, -90f, 0f),
            scale = new Vector3(0.166f, 0.083f, 0.0415f)
        },
        new PieceDef
        {
            id = "A3",
            localPosition = new Vector3(0.08425f, 0.022f, 0.0635f),
            localEuler = new Vector3(0f, -90f, -90f),
            scale = new Vector3(0.166f, 0.083f, 0.0415f)
        },
        new PieceDef
        {
            id = "A4",
            localPosition = new Vector3(-0.08175f, -0.022f, -0.0635f),
            localEuler = new Vector3(0f, -90f, 90f),
            scale = new Vector3(0.166f, 0.083f, 0.0415f)
        },
        new PieceDef
        {
            id = "A5",
            localPosition = new Vector3(-0.061f, 0.08175f, 0.022f),
            localEuler = new Vector3(-90f, -90f, 0f),
            scale = new Vector3(0.166f, 0.083f, 0.0415f)
        },
        new PieceDef
        {
            id = "A6",
            localPosition = new Vector3(0.022f, 0.0635f, -0.08425f),
            localEuler = Vector3.zero,
            scale = new Vector3(0.166f, 0.083f, 0.0415f)
        },
        new PieceDef
        {
            id = "B1",
            localPosition = new Vector3(0.0635f, -0.022f, -0.04275f),
            localEuler = new Vector3(0f, -90f, 0f),
            scale = new Vector3(0.1245f, 0.083f, 0.083f)
        },
        new PieceDef
        {
            id = "B2",
            localPosition = new Vector3(-0.04025f, -0.0635f, 0.022f),
            localEuler = Vector3.zero,
            scale = new Vector3(0.1245f, 0.083f, 0.083f)
        },
        new PieceDef
        {
            id = "B3",
            localPosition = new Vector3(0.022f, 0.0393f, 0.0635f),
            localEuler = new Vector3(0f, 0f, -90f),
            scale = new Vector3(0.1245f, 0.083f, 0.083f)
        },
        new PieceDef
        {
            id = "B4",
            localPosition = new Vector3(-0.061f, 0.0195f, 0.04275f),
            localEuler = new Vector3(0f, -90f, 0f),
            scale = new Vector3(0.1245f, 0.083f, 0.083f)
        },
        new PieceDef
        {
            id = "B5",
            localPosition = new Vector3(-0.0195f, -0.04275f, -0.0635f),
            localEuler = new Vector3(0f, 0f, -90f),
            scale = new Vector3(0.1245f, 0.083f, 0.083f)
        },
        new PieceDef
        {
            id = "B6",
            localPosition = new Vector3(0.04275f, 0.061f, -0.022f),
            localEuler = Vector3.zero,
            scale = new Vector3(0.1245f, 0.083f, 0.083f)
        }
    };

    private static readonly string[] ExpectedSequence =
    {
        "A1", "A2", "A3",
        "B1", "B2", "B3", "B4", "B5", "B6",
        "A4", "A5", "A6"
    };

    /// <summary>
    /// Inicializa directamente el modo de packing predefinido.
    /// No depende del menú inicial ni de piezas.json.
    /// </summary>
    private void Awake()
    {
        mainCamera = Camera.main;
        appStartTime = Time.realtimeSinceStartup;

        ConfigureTransparentBackground();

        if (!ResolveMarkerManager())
        {
            enabled = false;
            return;
        }

        ResetPackingState();
        markerMgr.enabled = true;

        Debug.Log("[CubicajeAR] Inicio directo. Esperando marcador PALLET.");
    }

    /// <summary>
    /// Se suscribe a los eventos del administrador de marcadores.
    /// </summary>
    private void OnEnable()
    {
        if (markerMgr != null)
        {
            markerMgr.markersChanged += OnMarkersChanged;
        }
    }

    /// <summary>
    /// Elimina la suscripción para evitar callbacks duplicados.
    /// </summary>
    private void OnDisable()
    {
        if (markerMgr != null)
        {
            markerMgr.markersChanged -= OnMarkersChanged;
        }
    }

    /// <summary>
    /// Configura el fondo para la visualización de realidad mixta.
    /// </summary>
    private void ConfigureTransparentBackground()
    {
        if (mainCamera != null)
        {
            mainCamera.clearFlags = CameraClearFlags.SolidColor;
            mainCamera.backgroundColor = new Color(0f, 0f, 0f, 0f);
        }

        RenderSettings.skybox = null;
    }

    /// <summary>
    /// Obtiene el ARMarkerManager asignado o lo localiza en la escena.
    /// </summary>
    private bool ResolveMarkerManager()
    {
        if (markerMgr == null)
        {
            markerMgr = FindObjectOfType<ARMarkerManager>();
        }

        if (markerMgr != null)
        {
            return true;
        }

        Debug.LogError("[CubicajeAR] No se encontró ARMarkerManager en la escena.");
        return false;
    }

    /// <summary>
    /// Restablece el flujo para iniciar esperando exclusivamente el pallet.
    /// </summary>
    private void ResetPackingState()
    {
        state = State.WaitingPallet;
        sequenceIndex = 0;
        lastQrText = string.Empty;
        lastQrTime = -999f;
        lastAnyQrProcessTime = -999f;
    }

    /// <summary>
    /// Procesa marcadores agregados o actualizados por OpenXR.
    /// </summary>
    private void OnMarkersChanged(ARMarkersChangedEventArgs eventArgs)
    {
        if (state == State.Done)
        {
            return;
        }

        foreach (ARMarker marker in eventArgs.added)
        {
            TryProcessMarker(marker);
        }

        foreach (ARMarker marker in eventArgs.updated)
        {
            TryProcessMarker(marker);
        }
    }

    /// <summary>
    /// Valida el marcador y lo dirige al paso correspondiente del flujo.
    /// </summary>
    private void TryProcessMarker(ARMarker marker)
    {
        if (marker == null)
        {
            return;
        }

        if (Time.realtimeSinceStartup < appStartTime + coldStartIgnoreSeconds)
        {
            return;
        }

        if (marker.trackingState != TrackingState.Tracking)
        {
            return;
        }

        string decodedText = markerMgr.GetDecodedString(marker.trackableId);
        if (string.IsNullOrWhiteSpace(decodedText))
        {
            return;
        }

        string qrText = NormalizeQrText(decodedText);
        if (!CanProcessQr(qrText))
        {
            return;
        }

        RegisterProcessedQr(qrText);

        if (state == State.WaitingPallet)
        {
            if (qrText == "PALLET")
            {
                ProcessPalletMarker(marker);
            }

            return;
        }

        if (state != State.PalletLocked || qrText == "PALLET")
        {
            return;
        }

        ProcessPieceMarker(marker, qrText);
    }

    /// <summary>
    /// Normaliza el contenido del QR y tolera un punto final en PALLET.
    /// </summary>
    private static string NormalizeQrText(string decodedText)
    {
        string normalized = decodedText.Trim().ToUpperInvariant();
        return normalized == "PALLET." ? "PALLET" : normalized;
    }

    /// <summary>
    /// Comprueba los filtros global y por texto para reducir lecturas duplicadas.
    /// </summary>
    private bool CanProcessQr(string qrText)
    {
        float now = Time.time;

        if (now - lastAnyQrProcessTime < qrGlobalCooldownSeconds)
        {
            return false;
        }

        if (qrText == lastQrText && now - lastQrTime < qrCooldown)
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Registra el QR aceptado por los filtros.
    /// </summary>
    private void RegisterProcessedQr(string qrText)
    {
        lastQrText = qrText;
        lastQrTime = Time.time;
        lastAnyQrProcessTime = Time.time;
    }

    /// <summary>
    /// Crea, posiciona, orienta y ancla el pallet virtual.
    /// </summary>
    private void ProcessPalletMarker(ARMarker marker)
    {
        if (pallet != null)
        {
            return;
        }

        pallet = CreateHollowPallet(
            "PALLET",
            palletOuterDims,
            wallThickness,
            palletWallColor,
            palletFloorColor,
            palletBackColorIdle,
            out palletFloor,
            out palletInnerBounds,
            out palletBackRenderer);

        PositionPalletAtMarker(marker);
        FacePalletTowardUser();

        if (pallet.GetComponent<ARAnchor>() == null)
        {
            pallet.AddComponent<ARAnchor>();
        }

        StartCoroutine(AnimatePalletIn());
        state = State.PalletLocked;

        Debug.Log($"[CubicajeAR] PALLET anclado. Siguiente pieza: {ExpectedSequence[sequenceIndex]}.");
    }

    /// <summary>
    /// Coloca el pallet sobre el marcador usando únicamente su orientación horizontal.
    /// </summary>
    private void PositionPalletAtMarker(ARMarker marker)
    {
        Vector3 markerPosition = marker.transform.position;
        Quaternion markerYaw = Quaternion.Euler(
            0f,
            marker.transform.rotation.eulerAngles.y,
            0f);

        Vector3 centerAboveMarker =
            markerPosition +
            Vector3.up * (palletOuterDims.y * 0.5f + baseHeightNudge);

        pallet.transform.SetPositionAndRotation(centerAboveMarker, markerYaw);
    }

    /// <summary>
    /// Orienta la cara abierta del pallet hacia la cámara al momento del anclaje.
    /// </summary>
    private void FacePalletTowardUser()
    {
        if (mainCamera == null || pallet == null)
        {
            return;
        }

        Vector3 directionToCamera =
            mainCamera.transform.position - pallet.transform.position;

        directionToCamera.y = 0f;

        if (directionToCamera.sqrMagnitude <= 0.0001f)
        {
            return;
        }

        Quaternion faceCamera =
            Quaternion.LookRotation(directionToCamera.normalized, Vector3.up) *
            Quaternion.Euler(0f, 180f, 0f);

        pallet.transform.rotation = faceCamera;
    }

    /// <summary>
    /// Valida una pieza contra la secuencia predefinida.
    /// </summary>
    private void ProcessPieceMarker(ARMarker marker, string pieceId)
    {
        if (!pieceId.StartsWith("A") && !pieceId.StartsWith("B"))
        {
            return;
        }

        if (sequenceIndex >= ExpectedSequence.Length)
        {
            return;
        }

        PieceDef pieceDefinition;
        if (!TryFindPieceDefinition(pieceId, out pieceDefinition))
        {
            Debug.LogWarning($"[CubicajeAR] QR de pieza no reconocida: {pieceId}.");
            return;
        }

        string expectedId = ExpectedSequence[sequenceIndex];

        if (pieceId != expectedId)
        {
            SpawnErrorAtMarker(marker, pieceDefinition);
            Debug.LogWarning(
                $"[CubicajeAR] Pieza incorrecta: {pieceId}. Se esperaba: {expectedId}.");
            return;
        }

        HandleCorrectPiece(marker, pieceDefinition);
    }

    /// <summary>
    /// Busca una pieza predefinida por su identificador.
    /// </summary>
    private static bool TryFindPieceDefinition(string id, out PieceDef definition)
    {
        for (int index = 0; index < PieceDefinitions.Length; index++)
        {
            if (PieceDefinitions[index].id == id)
            {
                definition = PieceDefinitions[index];
                return true;
            }
        }

        definition = default;
        return false;
    }

    /// <summary>
    /// Elimina el holograma anterior y anima la pieza correcta hacia su slot.
    /// </summary>
    private void HandleCorrectPiece(ARMarker marker, PieceDef definition)
    {
        ClearCurrentTravelPiece();

        Vector3 startPosition = marker.transform.position;
        Quaternion startRotation = marker.transform.rotation;

        Vector3 endPosition =
            pallet.transform.TransformPoint(definition.localPosition);

        Quaternion endRotation =
            pallet.transform.rotation *
            Quaternion.Euler(definition.localEuler);

        currentTravelPiece =
            CreatePieceHologram(
                definition.id + "_TRAVEL",
                definition.scale,
                startPosition,
                startRotation,
                travelColor);

        List<Vector3> path =
            BuildCurvedEntryPath(startPosition, endPosition, definition);

        currentTravelRoutine = StartCoroutine(
            MoveAlongPath(
                currentTravelPiece,
                path,
                pieceTravelSpeed,
                endRotation,
                OnCorrectPieceArrived));
    }

    /// <summary>
    /// Limpia la pieza verde anterior antes de mostrar la siguiente.
    /// </summary>
    private void ClearCurrentTravelPiece()
    {
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
    }

    /// <summary>
    /// Avanza la secuencia cuando la pieza termina su recorrido.
    /// </summary>
    private void OnCorrectPieceArrived()
    {
        sequenceIndex++;

        if (sequenceIndex >= ExpectedSequence.Length)
        {
            state = State.Done;
            StartCoroutine(PalletCompletedFeedback());
            markerMgr.enabled = false;

            Debug.Log("[CubicajeAR] Packing predefinido completado.");
            return;
        }

        Debug.Log(
            $"[CubicajeAR] Pieza colocada. Siguiente: {ExpectedSequence[sequenceIndex]}.");
    }

    /// <summary>
    /// Muestra temporalmente una pieza roja en la ubicación del QR incorrecto.
    /// </summary>
    private void SpawnErrorAtMarker(ARMarker marker, PieceDef definition)
    {
        if (currentErrorPiece != null)
        {
            Destroy(currentErrorPiece);
        }

        currentErrorPiece =
            CreatePieceHologram(
                definition.id + "_ERROR",
                definition.scale,
                marker.transform.position,
                marker.transform.rotation,
                errorColor);

        StartCoroutine(
            DestroyAfterSeconds(currentErrorPiece, errorLifetimeSeconds));
    }

    /// <summary>
    /// Crea un cubo holográfico sin collider.
    /// </summary>
    private static GameObject CreatePieceHologram(
        string objectName,
        Vector3 scale,
        Vector3 worldPosition,
        Quaternion worldRotation,
        Color color)
    {
        GameObject hologram =
            GameObject.CreatePrimitive(PrimitiveType.Cube);

        hologram.name = objectName;
        hologram.transform.position = worldPosition;
        hologram.transform.rotation = worldRotation;
        hologram.transform.localScale = scale;

        Material material = CreateTransparentMaterial(color);
        Renderer renderer = hologram.GetComponent<Renderer>();

        if (renderer != null)
        {
            renderer.sharedMaterial = material;
        }

        Collider collider = hologram.GetComponent<Collider>();
        if (collider != null)
        {
            collider.enabled = false;
        }

        return hologram;
    }

    /// <summary>
    /// Destruye un holograma después del tiempo indicado.
    /// </summary>
    private IEnumerator DestroyAfterSeconds(GameObject target, float seconds)
    {
        yield return new WaitForSeconds(seconds);

        if (target != null)
        {
            Destroy(target);
        }

        if (target == currentErrorPiece)
        {
            currentErrorPiece = null;
        }
    }

    /// <summary>
    /// Construye el pallet hueco con piso, fondo y paredes laterales.
    /// </summary>
    private static GameObject CreateHollowPallet(
        string objectName,
        Vector3 outerDimensions,
        float thickness,
        Color wallColor,
        Color floorColor,
        Color backColor,
        out Transform floor,
        out Bounds innerBounds,
        out Renderer backRenderer)
    {
        GameObject root = new GameObject(objectName);

        Material wallMaterial = CreateTransparentMaterial(wallColor);
        Material floorMaterial = CreateTransparentMaterial(floorColor);
        Material backMaterial = CreateTransparentMaterial(backColor);

        GameObject floorObject = CreatePalletPart(
            "Floor",
            root.transform,
            new Vector3(outerDimensions.x, thickness, outerDimensions.z),
            new Vector3(
                0f,
                -outerDimensions.y * 0.5f + thickness * 0.5f,
                0f),
            floorMaterial);

        GameObject backObject = CreatePalletPart(
            "Back",
            root.transform,
            new Vector3(outerDimensions.x, outerDimensions.y, thickness),
            new Vector3(
                0f,
                0f,
                outerDimensions.z * 0.5f - thickness * 0.5f),
            backMaterial);

        CreatePalletPart(
            "Left",
            root.transform,
            new Vector3(thickness, outerDimensions.y, outerDimensions.z),
            new Vector3(
                -outerDimensions.x * 0.5f + thickness * 0.5f,
                0f,
                0f),
            wallMaterial);

        CreatePalletPart(
            "Right",
            root.transform,
            new Vector3(thickness, outerDimensions.y, outerDimensions.z),
            new Vector3(
                outerDimensions.x * 0.5f - thickness * 0.5f,
                0f,
                0f),
            wallMaterial);

        floor = floorObject.transform;
        backRenderer = backObject.GetComponent<Renderer>();

        innerBounds = new Bounds(
            Vector3.zero,
            new Vector3(
                outerDimensions.x - 2f * thickness,
                outerDimensions.y - thickness,
                outerDimensions.z - thickness));

        return root;
    }

    /// <summary>
    /// Crea una parte del pallet y desactiva su collider.
    /// </summary>
    private static GameObject CreatePalletPart(
        string objectName,
        Transform parent,
        Vector3 localScale,
        Vector3 localPosition,
        Material material)
    {
        GameObject part = GameObject.CreatePrimitive(PrimitiveType.Cube);
        part.name = objectName;
        part.transform.SetParent(parent, false);
        part.transform.localScale = localScale;
        part.transform.localPosition = localPosition;

        Renderer renderer = part.GetComponent<Renderer>();
        if (renderer != null)
        {
            renderer.sharedMaterial = material;
        }

        Collider collider = part.GetComponent<Collider>();
        if (collider != null)
        {
            collider.enabled = false;
        }

        return part;
    }

    /// <summary>
    /// Crea un material compatible con transparencia.
    /// </summary>
    private static Material CreateTransparentMaterial(Color color)
    {
        Shader shader =
            Shader.Find("Legacy Shaders/Transparent/Diffuse") ??
            Shader.Find("Legacy Shaders/Diffuse");

        return new Material(shader)
        {
            color = color
        };
    }

    /// <summary>
    /// Anima la aparición del pallet desde escala cero.
    /// </summary>
    private IEnumerator AnimatePalletIn()
    {
        if (pallet == null)
        {
            yield break;
        }

        float elapsedTime = 0f;
        Vector3 startScale = Vector3.zero;
        Vector3 endScale = Vector3.one;

        pallet.transform.localScale = startScale;

        while (elapsedTime < palletPopDuration)
        {
            elapsedTime += Time.deltaTime;
            float normalizedTime =
                Mathf.Clamp01(elapsedTime / palletPopDuration);

            normalizedTime =
                normalizedTime *
                normalizedTime *
                (3f - 2f * normalizedTime);

            pallet.transform.localScale =
                Vector3.Lerp(startScale, endScale, normalizedTime);

            yield return null;
        }

        pallet.transform.localScale = endScale;
    }

    /// <summary>
    /// Construye una trayectoria curva desde el QR hasta el slot del pallet.
    /// </summary>
    private List<Vector3> BuildCurvedEntryPath(
        Vector3 startPosition,
        Vector3 endPosition,
        PieceDef definition)
    {
        List<Vector3> points = new List<Vector3>();

        float frontLocalZ =
            -palletOuterDims.z * 0.5f - entryFrontOffset;

        float localY = Mathf.Clamp(
            definition.localPosition.y,
            -palletInnerBounds.extents.y + innerMargin,
            palletInnerBounds.extents.y - innerMargin);

        float localX = Mathf.Clamp(
            definition.localPosition.x,
            -palletInnerBounds.extents.x + innerMargin,
            palletInnerBounds.extents.x - innerMargin);

        Vector3 entryLocal =
            new Vector3(localX, localY, frontLocalZ);

        Vector3 entryWorld =
            pallet.transform.TransformPoint(entryLocal);

        List<Vector3> firstSegment =
            BuildBezierSegment(startPosition, entryWorld, curveDropFactor);

        List<Vector3> secondSegment =
            BuildBezierSegment(
                entryWorld,
                endPosition,
                curveDropFactor * 0.7f);

        points.AddRange(firstSegment);

        for (int index = 1; index < secondSegment.Count; index++)
        {
            points.Add(secondSegment[index]);
        }

        return points;
    }

    /// <summary>
    /// Construye un segmento Bézier cuadrático con caída vertical.
    /// </summary>
    private List<Vector3> BuildBezierSegment(
        Vector3 start,
        Vector3 end,
        float dropFactor)
    {
        float distance = Vector3.Distance(start, end);
        Vector3 midpoint = (start + end) * 0.5f;
        Vector3 controlPoint =
            midpoint + Vector3.down * (distance * dropFactor);

        return SampleQuadraticBezier(
            start,
            controlPoint,
            end,
            curveSamplesPerSegment);
    }

    /// <summary>
    /// Muestrea una curva Bézier cuadrática.
    /// </summary>
    private static List<Vector3> SampleQuadraticBezier(
        Vector3 start,
        Vector3 control,
        Vector3 end,
        int samples)
    {
        int safeSamples = Mathf.Max(2, samples);
        List<Vector3> points =
            new List<Vector3>(safeSamples + 1);

        for (int index = 0; index <= safeSamples; index++)
        {
            float time = index / (float)safeSamples;
            float inverseTime = 1f - time;

            Vector3 point =
                inverseTime * inverseTime * start +
                2f * inverseTime * time * control +
                time * time * end;

            points.Add(point);
        }

        return points;
    }

    /// <summary>
    /// Mueve un objeto a velocidad aproximadamente constante a lo largo de la ruta.
    /// </summary>
    private IEnumerator MoveAlongPath(
        GameObject target,
        List<Vector3> path,
        float speed,
        Quaternion endRotation,
        Action onArrive)
    {
        if (target == null || path == null || path.Count < 2)
        {
            yield break;
        }

        float totalLength = CalculatePathLength(path);

        if (totalLength < 0.001f)
        {
            target.transform.position = path[path.Count - 1];
            target.transform.rotation = endRotation;
            onArrive?.Invoke();
            yield break;
        }

        float traveledDistance = 0f;

        while (traveledDistance < totalLength && target != null)
        {
            traveledDistance += speed * Time.deltaTime;

            float clampedDistance =
                Mathf.Clamp(traveledDistance, 0f, totalLength);

            SetTransformAtPathDistance(
                target.transform,
                path,
                clampedDistance,
                totalLength,
                endRotation);

            yield return null;
        }

        if (target != null)
        {
            target.transform.position = path[path.Count - 1];
            target.transform.rotation = endRotation;
        }

        onArrive?.Invoke();
        currentTravelRoutine = null;
    }

    /// <summary>
    /// Calcula la longitud total de una ruta.
    /// </summary>
    private static float CalculatePathLength(List<Vector3> path)
    {
        float totalLength = 0f;

        for (int index = 1; index < path.Count; index++)
        {
            totalLength +=
                Vector3.Distance(path[index - 1], path[index]);
        }

        return totalLength;
    }

    /// <summary>
    /// Ubica y orienta el objeto en una distancia determinada de la ruta.
    /// </summary>
    private static void SetTransformAtPathDistance(
        Transform target,
        List<Vector3> path,
        float targetDistance,
        float totalLength,
        Quaternion endRotation)
    {
        float accumulatedDistance = 0f;

        for (int index = 1; index < path.Count; index++)
        {
            Vector3 segmentStart = path[index - 1];
            Vector3 segmentEnd = path[index];
            float segmentLength =
                Vector3.Distance(segmentStart, segmentEnd);

            if (segmentLength <= 0.00001f)
            {
                continue;
            }

            if (accumulatedDistance + segmentLength >= targetDistance)
            {
                float segmentTime =
                    Mathf.Clamp01(
                        (targetDistance - accumulatedDistance) /
                        segmentLength);

                target.position =
                    Vector3.Lerp(segmentStart, segmentEnd, segmentTime);

                float routeTime =
                    Mathf.Clamp01(targetDistance / totalLength);

                target.rotation =
                    Quaternion.Slerp(
                        target.rotation,
                        endRotation,
                        routeTime);

                return;
            }

            accumulatedDistance += segmentLength;
        }
    }

    /// <summary>
    /// Cambia el fondo del pallet a verde fuerte al completar la secuencia.
    /// </summary>
    private IEnumerator PalletCompletedFeedback()
    {
        if (palletBackRenderer == null)
        {
            yield break;
        }

        Material material = palletBackRenderer.material;
        Color initialColor = material.color;
        float elapsedTime = 0f;
        const float duration = 0.35f;

        while (elapsedTime < duration)
        {
            elapsedTime += Time.deltaTime;
            float normalizedTime =
                Mathf.Clamp01(elapsedTime / duration);

            material.color =
                Color.Lerp(
                    initialColor,
                    palletBackStrongGreen,
                    normalizedTime);

            yield return null;
        }

        material.color = palletBackStrongGreen;
    }
}
