//  Generated from Pet Store
//  Requires TypeScript target ES2015 or higher, or include ES2015 in lib

export interface ClientOptions
{
    baseUrl?: string;
    credentials?: RequestCredentials;
    headers?: Record<string, string>;
}

export interface RequestOptions
{
    params?: Record<string, string | string[]>;
    body?: unknown;
    headers?: Record<string, string>;
    signal?: AbortSignal;
}
/**
 * A pet
 */

export interface Pet
{
    name: string;
}

export interface PetStoreClient
{
    get: Get;
    post: Post;
}

export interface Get
{
    pets(): Promise<Pet[]>;
}

export interface Post
{
    authLogin(): Promise<void>;
}

export function createClient(options: ClientOptions = {}): PetStoreClient
{
    if (!options.baseUrl) {
        throw new Error('baseUrl is required in ClientOptions');
    }
    const baseUrl = options.baseUrl;
    const credentials = options.credentials;
    const headers = options.headers || {};

    async function request<T>(path: string, method: string, init?: RequestOptions): Promise<T> {
        const base = baseUrl.replace(/\/$/, "");
        const url = new URL(path, base || undefined);
        if (init?.params) {
            const entries = Object.entries(init.params);
            for (const [key, value] of entries) {
                if (Array.isArray(value)) {
                    for (const v of value) { url.searchParams.append(key, v); }
                } else {
                    url.searchParams.append(key, value);
                }
            }
        }

        const requestHeaders: Record<string, string> = { ...headers, ...(init?.headers ?? {}) };
        let body: BodyInit | undefined;
        if (init?.body !== undefined) {
            if (typeof init.body === "object" && init.body !== null && !(init.body instanceof FormData) && !(init.body instanceof Blob) && !(init.body instanceof ArrayBuffer)) {
                if (!("Content-Type" in requestHeaders)) {
                    requestHeaders["Content-Type"] = "application/json";
                }
                body = JSON.stringify(init.body);
            } else {
                body = init.body as BodyInit;
            }
        }

        const response = await fetch(url.href, {
            method,
            credentials,
            headers: requestHeaders,
            body,
            signal: init?.signal,
        });

        if (!response.ok) {
            const errorText = await response.text().catch(() => "");
            throw new Error(`HTTP ${response.status} ${response.statusText}: ${errorText}`);
        }

        const text = await response.text();
        return (text ? JSON.parse(text) : undefined) as T;
    }

    return {
        get: {
            /** List pets */
            async pets(signal?: AbortSignal): Promise<Pet[]> {
                return request<Pet[]>('/pets', 'get', { signal });
            },
        },

        post: {
            async authLogin(signal?: AbortSignal): Promise<void> {
                return request<void>('/api/auth/login', 'post', { signal });
            },
        },
    };
}
