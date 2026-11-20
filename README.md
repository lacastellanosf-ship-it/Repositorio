# Citas

Aplicación de escritorio para Windows con agenda diaria y almacenamiento local en SQLite.

## Ejecutar

Requiere el SDK de .NET 8:

```powershell
dotnet run --project Citaspp.csproj
```

La base de datos se crea en `%LOCALAPPDATA%\Citaspp\citas.db`.

## Versión web en pruebas

La versión web se puede ejecutar localmente con el servidor ASP.NET Core:

```powershell
dotnet run --project Citaspp.Web\Citaspp.Web\Citaspp.Web.csproj --launch-profile http
```

Abre `http://localhost:5020`. La agenda permite navegar por semanas y meses, y crear, editar o eliminar citas. La API y su documentación OpenAPI están disponibles en `/swagger`.

Sin configurar Firebase, la agenda del navegador guarda sus datos en IndexedDB, separados por navegador y dispositivo. La API de pruebas del servidor mantiene una base SQLite en `%LOCALAPPDATA%\Citaspp\citas.db`; se puede cambiar su ruta con `CITASPP_DATABASE_PATH`.

## Prueba pública en GitHub Pages

El flujo de GitHub Actions publica automáticamente la versión WebAssembly estática al subir cambios a `main`. La aplicación y el código serán públicos. Sin configurar Firebase, las citas permanecen en el almacenamiento local del navegador y no se envían a GitHub.

Antes del primer despliegue, en el repositorio abre **Settings > Pages** y elige **GitHub Actions** como origen de publicación. La página estará en `https://<usuario>.github.io/<repositorio>/`.

Al configurar Firebase, las citas se guardan en Cloud Firestore y se actualizan en tiempo real entre dispositivos. Los trabajadores, servicios, festivos y horario todavía se guardan localmente en cada navegador.

### Activar Firebase Firestore

1. Crea un proyecto Firebase, registra una aplicación web y activa **Cloud Firestore**.
2. En **Authentication > Sign-in method**, activa **Correo electrónico/contraseña**. Crea manualmente los usuarios autorizados; la aplicación no ofrece registro público.
3. En **Configuración del proyecto > Tus aplicaciones**, copia la configuración web en `Citaspp.Web\Citaspp.Web.Client\wwwroot\firebase-config.js`, sustituyendo los campos `PEGA_AQUI_...`.
4. En **Authentication > Users**, confirma que el UID de cada usuario autorizado está incluido en la lista de `Citaspp.Web\firestore.rules`. La primera cuenta ya está añadida; agrega más UID a esa lista si hacen falta.
5. Publica las reglas desde la carpeta `Citaspp.Web`:

   ```powershell
   firebase deploy --only firestore:rules --project <project-id>
   ```

6. En Firebase Authentication, añade `lacastellanosf-ship-it.github.io` a los dominios autorizados.
7. Publica los cambios de la aplicación en GitHub Pages. Al abrir la página, inicia sesión con una cuenta cuyo UID esté autorizado.

La configuración web de Firebase no es una contraseña; el control de acceso lo aplican las reglas de Firestore. No pongas contraseñas ni claves privadas de cuentas de servicio en `firebase-config.js`, y no cambies las reglas para permitir acceso público. En el primer inicio de sesión, si `eventos` está vacío, las citas de IndexedDB de ese dispositivo se importan una sola vez. Conviene hacerlo primero desde el dispositivo que contiene la agenda que quieras conservar. Los datos de otros dispositivos no se importan si la colección ya contiene citas.

## Funciones

- Agenda en franjas de 15 minutos, inicialmente para dos trabajadores.
- Agenda semanal navegable y calendario para elegir la fecha.
- Alta, edición y eliminación de citas con cliente, teléfono, trabajador, servicio, fecha y hora.
- Nombres y colores de agenda personalizables para cada trabajador.
- Validación del teléfono: 9 dígitos nacionales o 12 dígitos con prefijo internacional.
- Duración de cada cita según el servicio y validación de solapes y horario.
- Configuración de días y horas laborables, número de trabajadores y servicios.
- Festivos locales guardados en la base de datos y editables desde Configuración.
- Importación de festivos desde CSV UTF-8. Columnas `fecha,nombre`; fechas `AAAA-MM-DD` o `DD/MM/AAAA`, con coma o punto y coma. Puede incluir cabecera:

```csv
fecha,nombre
2026-01-01,Año Nuevo
2026-01-06,Epifanía del Señor
```

La primera ejecución precarga los festivos de León para 2026. Los calendarios de años posteriores deben añadirse en Configuración; la descarga y actualización automática desde una fuente oficial queda pendiente.
