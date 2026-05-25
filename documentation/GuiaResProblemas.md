# Guía de Resolución de Problemas - CubicajeHL3

Esta guía analiza el código fuente de la aplicación **CubicajeHL3** para HoloLens 2 y detalla las razones por las cuales tu prueba inicial pudo fallar (no escaneó códigos ni mostró nada tras iniciar).

---

## 🔍 Análisis Técnico del Código

Al revisar el código fuente del proyecto, encontramos que la aplicación está construida sobre **Unity 2022.3** con **MRTK3** y **OpenXR**, y consta de dos flujos de trabajo principales que se ejecutan en la misma escena (`SampleScene.unity`):

1. **Flujo 1: Escaneo y Registro de Catálogo de Piezas (`PieceScanManager.cs`)**
   - Escucha marcadores QR con formato de dimensiones: `ID;alto_m;ancho_m;fondo_m` (ej. `A1;0.166;0.083;0.0415`).
   - Muestra un cubo holográfico **cian** temporal y guarda automáticamente los datos de las piezas en un archivo local llamado `piezas.json` dentro del almacenamiento persistente del dispositivo (`Application.persistentDataPath`).

2. **Flujo 2: Guía de Ensamblado en Pallet (`CubicajeARManager.cs`)**
   - Escucha un marcador QR con el texto exacto `PALLET`.
   - Lee el archivo `piezas.json` generado en el Flujo 1, ejecuta el motor de cubicaje 3D (`CubicajeEngine.cs`) para calcular la distribución óptima y proyecta un **pallet holográfico hueco**.
   - Guía al usuario a escanear los IDs simples de las piezas (ej. `A1`) en un orden específico, animando las piezas correctas en **verde** y mostrando en **rojo** los escaneos incorrectos.

---

## 🛑 Posibles Causas del Fallo en tu Prueba

A partir del comportamiento que describes ("la app inició pero luego no mostró nada y tampoco escaneó códigos"), estas son las causas más probables:

### 1. No se presionó el botón "Aceptar" en el Menú de Inicio (MRTK3)
- **Comportamiento en el código (`StartMenuManager.cs`):** Al arrancar la escena, se muestra un menú flotante en forma de cartelera. Este script deshabilita inmediatamente tanto el detector de marcadores como el gestor de escaneo:
  ```csharp
  if (scanManager != null) scanManager.enabled = false;
  if (markerManager != null) markerManager.enabled = false;
  ```
- **Consecuencia:** Hasta que el usuario no interactúe físicamente con este menú holográfico y presione el botón **Aceptar** (Accept), **el HoloLens 2 tiene las cámaras de escaneo QR apagadas**. No detectará absolutamente ningún código.
- **Solución:** Busca el menú flotante al iniciar la app y presiona el botón holográfico "Aceptar" usando el gesto de pulsación del HoloLens 2.

### 2. Intentar escanear el QR del Pallet primero sin tener piezas registradas
- **Comportamiento en el código (`CubicajeARManager.cs`):** Cuando escaneas el código QR con el texto `PALLET`, el sistema intenta inicializar el pallet llamando a la función `LoadAndSolve()`:
  ```csharp
  private bool LoadAndSolve()
  {
      string path = Path.Combine(Application.persistentDataPath, "piezas.json");
      if (!File.Exists(path))
      {
          Debug.LogError($"[CubicajeAR] piezas.json no encontrado en: {path}");
          return false;
      }
      ...
  ```
- **Consecuencia:** Si la aplicación se acaba de instalar o nunca se ha completado el "Flujo 1" (escaneo de dimensiones), el archivo `piezas.json` **no existe** en el dispositivo. Al no existir, la inicialización del pallet aborta en silencio. El pallet holográfico no se genera, no se muestra nada en la escena y la aplicación parece congelada.
- **Solución:** Debes escanear primero al menos una pieza con formato de dimensiones (ver sección de formatos abajo) para que se cree el archivo `piezas.json`, o bien transferir manualmente un archivo `piezas.json` válido al almacenamiento de la aplicación a través del Portal de Dispositivos de HoloLens (HoloLens Device Portal).

### 3. Uso de formatos de código QR incorrectos
El sistema utiliza filtros muy específicos para procesar los códigos. Si los QRs impresos no cumplen exactamente con la sintaxis, serán ignorados por completo:
- **Para registrar piezas (Fase de Catálogo):** Espera `ID;alto;ancho;fondo` (separados por punto y coma, con números decimales usando punto). Si escaneas un código que solo dice `A1` o `B2`, el sistema lo registrará como "mal formado" y lo ignorará.
- **Para el Pallet (Fase de Ensamblado):** Espera exactamente el texto `PALLET` o `PALLET.`.
- **Para colocar piezas (Fase de Ensamblado):** Espera el ID simple de la pieza, por ejemplo `A1` (sin punto y coma). Si escaneas el código de dimensiones completo aquí, no lo reconocerá como la pieza correcta.

### 4. Filtros de Cooldown (Anti-Spam) restrictivos
- **Comportamiento en el código:** La aplicación incluye dos variables de cooldown para evitar procesar lecturas repetidas por error:
  - `qrCooldown` (0.4 segundos): Tiempo mínimo para volver a leer el **mismo** código QR.
  - `qrGlobalCooldownSeconds` (2.0 segundos): Tiempo mínimo que debe transcurrir entre **cualquier** lectura de QR.
- **Consecuencia:** Si escaneas un código QR y rápidamente intentas escanear otro en menos de 2 segundos, el sistema descartará el segundo escaneo de manera silenciosa.
- **Solución:** Al realizar las pruebas, tómate al menos **2 o 3 segundos de pausa** entre el escaneo de cada código QR para que el cooldown global se reinicie.

### 5. Problemas de tracking o iluminación en el HoloLens 2
- El sistema de detección de QR de MRTK3/OpenXR depende de una buena iluminación de la habitación y de que las cámaras del HoloLens distingan claramente el contraste del código QR. Si estás probando en un entorno oscuro o los códigos son demasiado pequeños o están borrosos, la cámara no los registrará.

---

## 🛠️ Lista de Verificación para que la App Funcione

1. **Limpia o inicializa los datos:** Asegúrate de que el HoloLens tenga acceso a internet/cámara.
2. **Inicia la app:** Colócate el visor. Al iniciar, busca a tu alrededor el menú flotante de MRTK3.
3. **Acepta el inicio:** Mira el botón **Aceptar** del menú y haz click en él con tus dedos (Air Tap o toque directo). El menú desaparecerá, indicando que el escaneo ya está activo.
4. **Registra las piezas (Paso Obligatorio Primero):**
   - Apunta con tu mirada a un código QR que tenga el formato de dimensiones (ej: `A1;0.166;0.083;0.0415`).
   - Deberías ver aparecer un cubo de color **cian (azul claro)** sobre el QR por 1 segundo. Esto confirma que la pieza `A1` fue registrada y guardada en `piezas.json`.
5. **Genera el Pallet:**
   - Una vez escaneadas todas las piezas que vas a usar, apunta al código QR que dice `PALLET`.
   - Se leerá el archivo `piezas.json`, se calculará la distribución y **aparecerá un contenedor holográfico translúcido (el pallet)** flotando justo encima del código QR del pallet.
6. **Realiza el ensamblado:**
   - Escanea el código QR de la primera pieza física que te pida la secuencia (puedes ver la consola de depuración para saber cuál es, o seguir el orden lógico que calcula el algoritmo: de abajo a arriba, de atrás a adelante).
   - Al escanear el QR correcto de la pieza (ej: QR que solo tiene el texto `A1`), verás un cubo **verde** que viaja suavemente en curva hacia su posición dentro del pallet holográfico.
