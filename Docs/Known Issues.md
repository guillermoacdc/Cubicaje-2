# Known Issues

## Estado del baseline

- El proyecto compila correctamente para UWP ARM64.
- La aplicación se instala y ejecuta correctamente en HoloLens 2.
- El flujo funcional es parcialmente operativo en el dispositivo.
- El runtime OpenXR inicializa correctamente.
- El reconocimiento de códigos QR y la persistencia en JSON funcionan correctamente.
- Persisten problemas de interacción MRTK y un componente con un script faltante.

---

## Problemas observados

### Inicio de la aplicación

**Estado:** Parcialmente funcional.

Observaciones:

- La aplicación inicia correctamente.
- El menú inicial se muestra correctamente.
- Se detectó un componente con un script faltante durante el arranque.

**Mensaje registrado:**

```text
The referenced script (Unknown) on this Behaviour is missing!
The referenced script on this Behaviour (Game Object '<null>') is missing!
```

**Pendiente**

- Identificar el GameObject que contiene el componente faltante.
- Determinar si el problema proviene de un prefab, una escena o un script eliminado.

---

### Interacción MRTK

**Estado:** Parcialmente funcional.

Observaciones:

- La interacción mediante ray (far interaction) funciona parcialmente.
- La interacción cercana (near interaction) continúa presentando problemas.
- Los botones Accept y Cancel no siempre responden de manera consistente.

Pendiente:

- Revisar la configuración de StatefulInteractable.
- Verificar PressableButton y EventSystem.
- Confirmar la configuración de XR Interaction Toolkit.

---

### Inicialización de OpenXR

**Estado:** Correcto.

El runtime OpenXR inicia correctamente.

Se observa en el log:

- XRGeneralSettings inicializado.
- OpenXR Display Provider inicializado.
- OpenXR Input Provider inicializado.
- Microsoft OpenXR Input Extension inicializado.
- Microsoft OpenXR Mesh Extension inicializado.
- La sesión XR alcanza los estados:

```text
XR_SESSION_STATE_READY
XR_SESSION_STATE_VISIBLE
XR_SESSION_STATE_FOCUSED
```

No se registran errores durante la inicialización del runtime.

---

### Escaneo de códigos QR

**Estado:** Funcional.

Durante la prueba se comprobó que `PieceScanManager` procesa correctamente códigos QR.

Secuencia observada en el log:

1. A1
2. B1
3. A2

Después de cada lectura:

- La colección de piezas se actualiza correctamente.
- Se genera correctamente el archivo `piezas.json`.
- El archivo es almacenado correctamente en `LocalState`.

Contenido final registrado:

```text
A1
A2
B1
```

No se observaron errores durante la serialización del archivo JSON.

Pendiente:

- Confirmar mediante una prueba dedicada la lectura explícita del marcador `PALLET`.

---

### Cubicaje

**Estado:** Parcialmente funcional.

El log confirma que `CubicajeARManager` recibe eventos provenientes del sistema de marcadores.

Se observan llamadas como:

```text
[CubicajeAR] TryProcessMarker(...)
```

y la máquina de estados alcanza el estado:

```text
WaitingPallet
```

No se registran excepciones durante esta fase.

Pendiente:

- Validar completamente el flujo de ensamblado.
- Validar el cambio de estado después del reconocimiento del pallet.

---

### Renderizado

**Estado:** Parcialmente funcional.

Los hologramas se generan correctamente.

Observaciones durante las pruebas:

- Bajo contraste.
- Transparencia elevada.
- En algunas condiciones de iluminación resulta difícil apreciar los hologramas.

---

## Excepciones

Durante esta ejecución **no** se observaron:

- NullReferenceException
- MissingReferenceException
- InvalidOperationException
- StackOverflowException
- AccessViolationException

---

## Hallazgos relevantes del log

### Confirmado

- La aplicación inicia correctamente.
- OpenXR inicializa correctamente.
- El runtime alcanza el estado `FOCUSED`.
- Los proveedores Display, Input y Mesh son registrados correctamente.
- PieceScanManager procesa códigos QR.
- Se genera correctamente `piezas.json`.
- Se verificó el registro de las piezas:
  - A1
  - B1
  - A2
- CubicajeARManager recibe eventos del sistema de marcadores.

### Pendiente

- Identificar el componente con `Missing Script`.
- Confirmar el reconocimiento explícito del marcador `PALLET`.
- Verificar el flujo completo de ensamblado.
- Resolver la interacción cercana de MRTK.
- Mejorar la visibilidad de los hologramas.

---

## Próxima iteración

La siguiente implementación eliminará la dependencia del menú inicial y concentrará el flujo exclusivamente en el modo de **packing con datos predefinidos**:

1. Inicio directo de la aplicación.
2. Espera del marcador `PALLET`.
3. Creación y anclaje del pallet virtual.
4. Ensamblado guiado utilizando las piezas predefinidas.