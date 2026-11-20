import { firebaseAdminUids, firebaseConfig } from "./firebase-config.js";

const sdkBaseUrl = "https://www.gstatic.com/firebasejs/13.4.0";
let authSdk;
let firestoreSdk;
let auth;
let database;
let initialization;
let nextSubscriptionId = 1;
const subscriptions = new Map();

function isConfigured() {
    const requiredKeys = ["apiKey", "authDomain", "projectId", "messagingSenderId", "appId"];
    return requiredKeys.every(key => {
        const value = firebaseConfig[key];
        return typeof value === "string" && value.length > 0 && !value.startsWith("PEGA_AQUI_");
    });
}

async function ensureInitialized() {
    if (!isConfigured()) {
        return false;
    }

    if (!initialization) {
        initialization = (async () => {
            const [appSdk, loadedAuthSdk, loadedFirestoreSdk] = await Promise.all([
                import(`${sdkBaseUrl}/firebase-app.js`),
                import(`${sdkBaseUrl}/firebase-auth.js`),
                import(`${sdkBaseUrl}/firebase-firestore.js`)
            ]);
            const app = appSdk.initializeApp(firebaseConfig);
            authSdk = loadedAuthSdk;
            firestoreSdk = loadedFirestoreSdk;
            auth = authSdk.getAuth(app);
            database = firestoreSdk.getFirestore(app);
        })();
    }

    try {
        await initialization;
    } catch (error) {
        initialization = null;
        throw error;
    }

    return true;
}

function waitForUser() {
    return new Promise((resolve, reject) => {
        let unsubscribe = () => {};
        unsubscribe = authSdk.onAuthStateChanged(
            auth,
            user => {
                unsubscribe();
                resolve(user);
            },
            error => {
                unsubscribe();
                reject(error);
            });
    });
}

function isAdmin(user = auth?.currentUser) {
    return Boolean(user && firebaseAdminUids.includes(user.uid));
}

async function isAuthorized(user) {
    if (!user) {
        return false;
    }

    if (isAdmin(user)) {
        return true;
    }

    const reference = firestoreSdk.doc(database, "authorizedUsers", user.uid);
    const snapshot = await firestoreSdk.getDoc(reference);
    return snapshot.exists();
}

async function requireUser() {
    if (!await ensureInitialized()) {
        throw new Error("Firebase no está configurado.");
    }

    if (!auth.currentUser) {
        const user = await waitForUser();
        if (!user) {
            throw new Error("Inicia sesión para acceder a las citas de Firebase.");
        }
    }
}

async function requireAdmin() {
    await requireUser();
    if (!isAdmin()) {
        throw new Error("Solo el administrador puede gestionar los UID autorizados.");
    }
}

function configurationFrom(data) {
    const settings = data.settings ?? data.Settings;
    if (!settings) {
        throw new Error("La configuración compartida no contiene los datos de jornada.");
    }

    return {
        settings: {
            startTime: settings.startTime ?? settings.StartTime,
            endTime: settings.endTime ?? settings.EndTime,
            workDays: settings.workDays ?? settings.WorkDays,
            workerCount: Number(settings.workerCount ?? settings.WorkerCount)
        },
        workers: (data.workers ?? data.Workers ?? []).map(worker => ({
            id: Number(worker.id ?? worker.Id),
            name: worker.name ?? worker.Name,
            color: worker.color ?? worker.Color
        })),
        services: (data.services ?? data.Services ?? []).map(service => ({
            id: Number(service.id ?? service.Id),
            name: service.name ?? service.Name,
            durationMinutes: Number(service.durationMinutes ?? service.DurationMinutes)
        })),
        holidays: (data.holidays ?? data.Holidays ?? []).map(holiday => ({
            date: String(holiday.date ?? holiday.Date).slice(0, 10),
            name: holiday.name ?? holiday.Name
        }))
    };
}

async function readLocalConfiguration() {
    if (!globalThis.citasppStorage) {
        throw new Error("No se pudo acceder a la configuración local para inicializar Firestore.");
    }

    const storedSnapshot = await globalThis.citasppStorage.readSnapshot();
    const state = typeof storedSnapshot === "string"
        ? JSON.parse(storedSnapshot)
        : storedSnapshot;
    if (!state) {
        throw new Error("No hay una configuración local disponible para inicializar Firestore.");
    }

    return configurationFrom(state);
}

function serializeAppointment(appointment, id = appointment.id) {
    const startsAt = new Date(appointment.startsAt);
    const endsAt = new Date(appointment.endsAt);
    if (Number.isNaN(startsAt.getTime()) || Number.isNaN(endsAt.getTime())) {
        throw new Error("La fecha de la cita no es válida.");
    }

    return {
        id,
        customerName: appointment.customerName,
        phone: appointment.phone,
        workerId: appointment.workerId,
        serviceId: appointment.serviceId,
        serviceName: appointment.serviceName,
        durationMinutes: appointment.durationMinutes,
        startsAt: firestoreSdk.Timestamp.fromDate(startsAt),
        endsAt: firestoreSdk.Timestamp.fromDate(endsAt)
    };
}

function toAppointment(document) {
    const data = document.data();
    return {
        id: Number(data.id),
        customerName: data.customerName,
        phone: data.phone,
        workerId: Number(data.workerId),
        serviceId: Number(data.serviceId),
        serviceName: data.serviceName,
        durationMinutes: Number(data.durationMinutes),
        startsAt: data.startsAt.toDate().toISOString(),
        endsAt: data.endsAt.toDate().toISOString()
    };
}

async function migrateLocalAppointments() {
    const migrationRef = firestoreSdk.doc(database, "metadata", "local-migration");
    const migration = await firestoreSdk.getDoc(migrationRef);
    if (migration.exists()) {
        return 0;
    }

    const claimed = await firestoreSdk.runTransaction(database, async transaction => {
        const current = await transaction.get(migrationRef);
        if (current.exists()) {
            return false;
        }

        transaction.set(migrationRef, { status: "importing" });
        return true;
    });
    if (!claimed) {
        return 0;
    }

    try {
        const eventsRef = firestoreSdk.collection(database, "eventos");
        const existing = await firestoreSdk.getDocs(eventsRef);
        let migratedCount = 0;
        let highestId = 0;

        if (existing.empty && globalThis.citasppStorage) {
            const storedSnapshot = await globalThis.citasppStorage.readSnapshot();
            const localState = typeof storedSnapshot === "string"
                ? JSON.parse(storedSnapshot)
                : storedSnapshot;
            const localAppointments = localState?.appointments ?? localState?.Appointments ?? [];

            for (let offset = 0; offset < localAppointments.length; offset += 450) {
                const batch = firestoreSdk.writeBatch(database);
                const chunk = localAppointments.slice(offset, offset + 450);
                for (const appointment of chunk) {
                    const id = Number(appointment.id ?? appointment.Id);
                    if (!Number.isSafeInteger(id) || id < 1) {
                        throw new Error("Hay una cita local con identificador no válido.");
                    }

                    highestId = Math.max(highestId, id);
                    const eventRef = firestoreSdk.doc(eventsRef, String(id));
                    batch.set(eventRef, serializeAppointment({
                        customerName: appointment.customerName ?? appointment.CustomerName,
                        phone: appointment.phone ?? appointment.Phone,
                        workerId: appointment.workerId ?? appointment.WorkerId,
                        serviceId: appointment.serviceId ?? appointment.ServiceId,
                        serviceName: appointment.serviceName ?? appointment.ServiceName,
                        durationMinutes: appointment.durationMinutes ?? appointment.DurationMinutes,
                        startsAt: appointment.startsAt ?? appointment.StartsAt,
                        endsAt: appointment.endsAt ?? appointment.EndsAt
                    }, id));
                }

                await batch.commit();
                migratedCount += chunk.length;
            }
        }

        const counterRef = firestoreSdk.doc(database, "metadata", "counters");
        const counter = await firestoreSdk.getDoc(counterRef);
        const currentNextId = Number(counter.data()?.nextAppointmentId ?? 1);
        await firestoreSdk.setDoc(counterRef, {
            nextAppointmentId: Math.max(currentNextId, highestId + 1)
        }, { merge: true });
        await firestoreSdk.setDoc(migrationRef, {
            status: "complete",
            migratedCount,
            completedAt: firestoreSdk.serverTimestamp()
        });
        return migratedCount;
    } catch (error) {
        await firestoreSdk.deleteDoc(migrationRef);
        throw error;
    }
}

export async function initialize() {
    if (!await ensureInitialized()) {
        return { configured: false, authenticated: false, authorized: false, email: null, isAdmin: false };
    }

    const user = await waitForUser();
    const authorized = await isAuthorized(user);
    const migratedAppointments = user && authorized && isAdmin(user)
        ? await migrateLocalAppointments()
        : 0;
    return {
        configured: true,
        authenticated: Boolean(user),
        authorized,
        email: user?.email ?? null,
        migratedAppointments,
        isAdmin: isAdmin(user)
    };
}

export async function signIn(email, password) {
    if (!await ensureInitialized()) {
        throw new Error("Pega primero la configuración de Firebase en firebase-config.js.");
    }

    const credential = await authSdk.signInWithEmailAndPassword(auth, email, password);
    const authorized = await isAuthorized(credential.user);
    if (!authorized) {
        await authSdk.signOut(auth);
        throw new Error("Esta cuenta no está autorizada para acceder a la agenda.");
    }

    const migratedAppointments = isAdmin(credential.user)
        ? await migrateLocalAppointments()
        : 0;
    return {
        email: credential.user.email,
        migratedAppointments,
        isAdmin: isAdmin(credential.user),
        authorized
    };
}

export async function signOut() {
    await requireUser();
    await authSdk.signOut(auth);
}

export async function getAppointments(startText, endText) {
    await requireUser();
    const start = firestoreSdk.Timestamp.fromDate(new Date(startText));
    const end = firestoreSdk.Timestamp.fromDate(new Date(endText));
    const events = firestoreSdk.collection(database, "eventos");
    const appointments = firestoreSdk.query(
        events,
        firestoreSdk.where("startsAt", ">=", start),
        firestoreSdk.where("startsAt", "<", end));
    const snapshot = await firestoreSdk.getDocs(appointments);
    return snapshot.docs.map(toAppointment).sort((first, second) =>
        first.startsAt.localeCompare(second.startsAt));
}

export async function getAllAppointments() {
    await requireUser();
    const snapshot = await firestoreSdk.getDocs(firestoreSdk.collection(database, "eventos"));
    return snapshot.docs.map(toAppointment);
}

export async function getAppointment(id) {
    await requireUser();
    const reference = firestoreSdk.doc(database, "eventos", String(id));
    const snapshot = await firestoreSdk.getDoc(reference);
    return snapshot.exists() ? toAppointment(snapshot) : null;
}

export async function getConfiguration() {
    await requireUser();
    const reference = firestoreSdk.doc(database, "configuracion", "agenda");
    const existing = await firestoreSdk.getDoc(reference);
    if (existing.exists()) {
        return configurationFrom(existing.data());
    }

    const localConfiguration = await readLocalConfiguration();
    const storedConfiguration = await firestoreSdk.runTransaction(database, async transaction => {
        const snapshot = await transaction.get(reference);
        if (snapshot.exists()) {
            return snapshot.data();
        }

        if (!isAdmin()) {
            throw new Error("El administrador debe inicializar primero la configuración compartida.");
        }

        transaction.set(reference, localConfiguration);
        return localConfiguration;
    });
    return configurationFrom(storedConfiguration);
}

export async function saveConfigurationSections(sections) {
    await requireUser();
    const allowedSections = ["settings", "workers", "services", "holidays"];
    const values = {};
    for (const [section, value] of Object.entries(sections)) {
        if (!allowedSections.includes(section)) {
            throw new Error("La sección de configuración no es válida.");
        }

        values[section] = section === "settings"
            ? {
                startTime: value.startTime,
                endTime: value.endTime,
                workDays: value.workDays,
                workerCount: value.workerCount
            }
            : section === "workers"
                ? value.map(worker => ({
                    id: Number(worker.id ?? worker.Id),
                    name: worker.name ?? worker.Name,
                    color: worker.color ?? worker.Color
                }))
                : section === "services"
                    ? value.map(service => ({
                        id: Number(service.id ?? service.Id),
                        name: service.name ?? service.Name,
                        durationMinutes: Number(service.durationMinutes ?? service.DurationMinutes)
                    }))
                    : value.map(holiday => ({
                        date: holiday.date ?? holiday.Date,
                        name: holiday.name ?? holiday.Name
                    }));
    }

    await firestoreSdk.setDoc(
        firestoreSdk.doc(database, "configuracion", "agenda"),
        values,
        { merge: true });
}

export async function getAuthorizedUids() {
    await requireAdmin();
    const snapshot = await firestoreSdk.getDocs(
        firestoreSdk.collection(database, "authorizedUsers"));
    return [...new Set([
        ...firebaseAdminUids,
        ...snapshot.docs.map(document => document.id)
    ])].sort();
}

export async function addAuthorizedUid(uid) {
    await requireAdmin();
    const normalizedUid = String(uid ?? "").trim();
    if (!normalizedUid || normalizedUid.length > 128 || normalizedUid.includes("/")) {
        throw new Error("Introduce un UID válido de Firebase (1 a 128 caracteres, sin barras).");
    }

    if (firebaseAdminUids.includes(normalizedUid)) {
        throw new Error("Ese UID ya tiene acceso de administrador.");
    }

    const reference = firestoreSdk.doc(database, "authorizedUsers", normalizedUid);
    if ((await firestoreSdk.getDoc(reference)).exists()) {
        throw new Error("Ese UID ya está autorizado.");
    }

    await firestoreSdk.setDoc(reference, {
        addedAt: firestoreSdk.serverTimestamp(),
        addedBy: auth.currentUser.uid
    });
}

export async function createAppointment(appointment) {
    await requireUser();
    const counterRef = firestoreSdk.doc(database, "metadata", "counters");
    return await firestoreSdk.runTransaction(database, async transaction => {
        const counter = await transaction.get(counterRef);
        let id = Math.max(1, Number(counter.data()?.nextAppointmentId ?? 1));
        let eventRef = firestoreSdk.doc(database, "eventos", String(id));
        let event = await transaction.get(eventRef);
        while (event.exists()) {
            id++;
            eventRef = firestoreSdk.doc(database, "eventos", String(id));
            event = await transaction.get(eventRef);
        }

        if (!Number.isSafeInteger(id) || id >= 2147483647) {
            throw new Error("Se agotaron los identificadores de citas.");
        }

        transaction.set(counterRef, { nextAppointmentId: id + 1 }, { merge: true });
        transaction.set(eventRef, serializeAppointment(appointment, id));
        return id;
    });
}

export async function updateAppointment(appointment) {
    await requireUser();
    const reference = firestoreSdk.doc(database, "eventos", String(appointment.id));
    await firestoreSdk.updateDoc(reference, serializeAppointment(appointment));
}

export async function deleteAppointment(id) {
    await requireUser();
    await firestoreSdk.deleteDoc(firestoreSdk.doc(database, "eventos", String(id)));
}

export async function deleteAppointments(ids) {
    await requireUser();
    for (let offset = 0; offset < ids.length; offset += 450) {
        const batch = firestoreSdk.writeBatch(database);
        for (const id of ids.slice(offset, offset + 450)) {
            batch.delete(firestoreSdk.doc(database, "eventos", String(id)));
        }

        await batch.commit();
    }
}

export async function saveMovedAppointments(appointments) {
    await requireUser();
    for (let offset = 0; offset < appointments.length; offset += 450) {
        const batch = firestoreSdk.writeBatch(database);
        for (const appointment of appointments.slice(offset, offset + 450)) {
            batch.set(
                firestoreSdk.doc(database, "eventos", String(appointment.id)),
                serializeAppointment(appointment));
        }

        await batch.commit();
    }
}

export async function subscribeAppointments(startText, endText, dotNetReference) {
    await requireUser();
    const start = firestoreSdk.Timestamp.fromDate(new Date(startText));
    const end = firestoreSdk.Timestamp.fromDate(new Date(endText));
    const events = firestoreSdk.collection(database, "eventos");
    const appointments = firestoreSdk.query(
        events,
        firestoreSdk.where("startsAt", ">=", start),
        firestoreSdk.where("startsAt", "<", end));
    const subscriptionId = nextSubscriptionId++;
    const unsubscribe = firestoreSdk.onSnapshot(
        appointments,
        snapshot => {
            const values = snapshot.docs.map(toAppointment);
            dotNetReference.invokeMethodAsync("OnFirebaseAppointmentsChanged", values)
                .catch(error => console.error("No se pudo actualizar la agenda en la interfaz.", error));
        },
        error => {
            dotNetReference.invokeMethodAsync("OnFirebaseAppointmentsError", error.message)
                .catch(callbackError => console.error("No se pudo mostrar el error de sincronización.", callbackError));
        });
    subscriptions.set(subscriptionId, unsubscribe);
    return subscriptionId;
}

export async function unsubscribeAppointments(subscriptionId) {
    const unsubscribe = subscriptions.get(subscriptionId);
    if (unsubscribe) {
        unsubscribe();
        subscriptions.delete(subscriptionId);
    }
}

export async function subscribeConfiguration(dotNetReference) {
    await requireUser();
    const reference = firestoreSdk.doc(database, "configuracion", "agenda");
    const subscriptionId = nextSubscriptionId++;
    const unsubscribe = firestoreSdk.onSnapshot(
        reference,
        snapshot => {
            if (!snapshot.exists()) {
                return;
            }

            dotNetReference.invokeMethodAsync(
                "OnFirebaseConfigurationChanged",
                configurationFrom(snapshot.data()))
                .catch(error => console.error("No se pudo actualizar la configuración en la interfaz.", error));
        },
        error => {
            dotNetReference.invokeMethodAsync("OnFirebaseConfigurationError", error.message)
                .catch(callbackError => console.error("No se pudo mostrar el error de configuración.", callbackError));
        });
    subscriptions.set(subscriptionId, unsubscribe);
    return subscriptionId;
}

export async function unsubscribeConfiguration(subscriptionId) {
    const unsubscribe = subscriptions.get(subscriptionId);
    if (unsubscribe) {
        unsubscribe();
        subscriptions.delete(subscriptionId);
    }
}
