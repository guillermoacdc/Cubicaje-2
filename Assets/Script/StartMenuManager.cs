using System.Collections;
using Microsoft.MixedReality.OpenXR;
using UnityEngine;

public class StartMenuManager : MonoBehaviour
{
    [Header("Managers de AR")]
    public PieceScanManager scanManager;    // Gestor de escaneo de piezas (nuevo flujo)
    public ARMarkerManager markerManager;

    [Header("Menu MRTK3")]
    [Tooltip("Raiz visual del menu MRTK3. Si no se asigna, se usara este mismo GameObject.")]
    public GameObject menuVisualRoot;

    [Header("Ajustes")]
    public float distanceFromCamera = 1.5f;
    public float fadeDuration = 0.3f;
    public bool keepFacingUser = true;

    private bool isActiveMenu = true;

    private Transform MenuTransform => menuVisualRoot != null ? menuVisualRoot.transform : transform;

    private void Start()
    {
        Debug.Log("[StartMenu] Start() ejecutado.");

        if (scanManager != null) scanManager.enabled = false;
        if (markerManager != null) markerManager.enabled = false;

        if (menuVisualRoot != null)
        {
            menuVisualRoot.SetActive(true);
        }
        else
        {
            Debug.LogWarning("[StartMenu] No se asigno menuVisualRoot. Se usara el GameObject del menu.");
        }

        PlaceInFrontOfCamera();
        Debug.Log("[StartMenu] Menu MRTK3 listo en escena.");
    }

    private void Update()
    {
        if (!isActiveMenu || !keepFacingUser || Camera.main == null)
        {
            return;
        }

        Vector3 dir = MenuTransform.position - Camera.main.transform.position;
        if (dir.sqrMagnitude > 0.0001f)
        {
            MenuTransform.rotation = Quaternion.LookRotation(dir);
        }
    }

    private void PlaceInFrontOfCamera()
    {
        if (Camera.main == null)
        {
            Debug.LogWarning("[StartMenu] Camera.main no esta disponible para ubicar el menu.");
            return;
        }

        Transform cam = Camera.main.transform;
        MenuTransform.position = cam.position + cam.forward * distanceFromCamera;

        Vector3 dir = MenuTransform.position - cam.position;
        if (dir.sqrMagnitude > 0.0001f)
        {
            MenuTransform.rotation = Quaternion.LookRotation(dir);
        }
    }

    public void OnAccept()
    {
        if (!isActiveMenu)
        {
            return;
        }

        Debug.Log("[StartMenu] Aceptar presionado.");
        StartCoroutine(FadeOutAndActivate());
    }

    public void OnCancel()
    {
        Debug.Log("[StartMenu] Cancelar presionado. Cerrando aplicacion.");
        Application.Quit();
    }

    private IEnumerator FadeOutAndActivate()
    {
        isActiveMenu = false;

        if (fadeDuration > 0f)
        {
            yield return new WaitForSeconds(fadeDuration);
        }

        if (markerManager != null) markerManager.enabled = true;
        if (scanManager != null) scanManager.enabled = true;

        if (menuVisualRoot != null)
        {
            menuVisualRoot.SetActive(false);
        }

        Destroy(gameObject);
    }
}
