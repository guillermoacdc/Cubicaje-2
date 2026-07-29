# Procedimiento de Build del Proyecto (Estado Actual)

## Objetivo

Generar una compilación del proyecto para **HoloLens 2** a partir de la
escena **SampleScene**, utilizando **Unity 2022.3.62f1** y **Visual
Studio 2022**.

------------------------------------------------------------------------

## 1. Abrir el proyecto

1.  Abrir **Unity Hub**.
2.  Abrir el proyecto con la versión **Unity 2022.3.62f1**.

------------------------------------------------------------------------

## 2. Abrir la escena

Abrir la escena:

`Assets/Scenes/SampleScene.unity`

Guardar cualquier cambio antes de iniciar el proceso de compilación.

------------------------------------------------------------------------

## 3. Configurar la plataforma

Ir a:

**File → Build Settings**

Seleccionar:

**Universal Windows Platform**

Si esta plataforma **no está activa**, pulsar:

**Switch Platform**

y esperar a que Unity complete el cambio de plataforma.

------------------------------------------------------------------------

## 4. Verificar la escena en el Build

En **Scenes In Build** comprobar que la escena abierta esté incluida.

Si no aparece, pulsar:

**Add Open Scenes**

La escena **SampleScene** debe quedar como la escena de inicio (índice
0).

------------------------------------------------------------------------

## 5. Configuración del Build

En **Build Settings** configurar:

  Parámetro               Valor
  ----------------------- ----------------------------
  Platform                Universal Windows Platform
  Target Device           HoloLens (opcional en algunas interfaces)
  Architecture            ARM64
  Build Type              D3D Project
  Target SDK Version      Latest Installed
  Visual Studio Version   Visual Studio 2022
  Build and Run on        Local Machine

------------------------------------------------------------------------

## 6. Verificar OpenXR

Ir a:

**Edit → Project Settings → XR Plug-in Management**

Para **Universal Windows Platform** comprobar que **OpenXR** esté
habilitado.

------------------------------------------------------------------------

## 7. Verificar capacidades

Ir a:

**Edit → Project Settings → Player → Publishing Settings →
Capabilities**

Confirmar que permanezcan habilitadas las capacidades requeridas por el
proyecto, entre ellas:

-   WebCam
-   SpatialPerception
-   InternetClient

------------------------------------------------------------------------

## 8. Revisar la consola

Abrir:

**Window → General → Console**

-   Limpiar la consola.
-   Esperar a que Unity termine la compilación de scripts.
-   Confirmar que no existan errores de compilación.

------------------------------------------------------------------------

## 9. Crear la carpeta del Build

Crear una carpeta nueva en la raíz del proyecto, por ejemplo:

`Build_Current`

------------------------------------------------------------------------

## 10. Generar el Build

En **Build Settings** pulsar:

**Build**

Seleccionar la carpeta **Build_Current** y esperar a que Unity genere la
solución de Visual Studio.

------------------------------------------------------------------------

## 11. Abrir la solución en Visual Studio

Abrir el archivo `.sln` generado.

Seleccionar:

-   Configuración: **Release**
-   Arquitectura: **ARM64**
-   Destino: **Device** (USB) o **Remote Machine** (Wi-Fi)

------------------------------------------------------------------------

## 12. Compilar

Ejecutar:

**Build → Build Solution**

Verificar que la compilación finalice sin errores.

------------------------------------------------------------------------

## 13. Desplegar en HoloLens

Ejecutar:

**Build → Deploy Solution**

o presionar **F5** para compilar, desplegar y ejecutar.

Instrucciones detalladas pasos 11 a 13 en: https://learn.microsoft.com/en-us/training/modules/learn-mrtk-tutorials/1-7-exercise-hand-interaction-with-objectmanipulator

------------------------------------------------------------------------

## 14. Registrar el resultado

Después de cada prueba registrar:

-   Rama de Git.
-   Commit probado.
-   Versión de Unity.
-   Configuración del Build.
-   Resultado de la compilación.
-   Resultado del despliegue.
-   Resultado de la ejecución en HoloLens.
-   Errores observados.
