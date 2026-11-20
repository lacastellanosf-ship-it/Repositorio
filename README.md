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

La agenda del navegador guarda sus datos en IndexedDB, separados por navegador y dispositivo. No existe sincronización con MEGA ni entre dispositivos. La API de pruebas del servidor mantiene una base SQLite en `%LOCALAPPDATA%\Citaspp\citas.db`; se puede cambiar su ruta con `CITASPP_DATABASE_PATH`.

## Prueba pública en GitHub Pages

El flujo de GitHub Actions publica automáticamente la versión WebAssembly estática al subir cambios a `main`. La aplicación y el código serán públicos; las citas que se creen desde la página permanecen en el almacenamiento local del navegador y no se envían a GitHub.

Esta publicación no aloja la API ASP.NET Core ni habilita sincronización en la nube. No uses datos reales o sensibles en esta versión de pruebas.

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
