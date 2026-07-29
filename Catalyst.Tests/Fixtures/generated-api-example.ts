//  Generated from TRS Enterprise API
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

export interface GetMediaResponseItem
{
    uid: string;
    name: string;
    mimetype: string;
    size: number;
    url: string | null;
    createdAt: string;
}

export interface GetMediaSignedUrlResponse
{
    signedUrl: string;
}

export interface PostMediaUploadResponse
{
    uid: string;
    name: string;
    mimetype: string;
    size: number;
}

export interface DeleteMediaIdResponse
{
    success: boolean;
}

export interface PostRegisterRequest
{
    name: string;
    email: string;
    password: string;
    organizationName: string;
    callbackURL?: string;
}

export interface PostUserPermissionsSetRequest
{
    userId: string;
    permissions: string[];
}

export interface PostUserPermissionsAddRequest
{
    userId: string;
    permissions: string[];
}

export interface PostUserPermissionsRemoveRequest
{
    userId: string;
    permissions: string[];
}

export interface GetAppContextResponse
{
    hasSession: boolean;
    user: GetAppContextResponseUser | null;
    activeOrganizationId: string | null;
    organizations: GetAppContextResponseOrganizationsItem[];
    permissions: string[];
}

export interface GetAppContextResponseUser
{
    id: string;
    name: string;
    email: string;
    avatar?: string | null;
}

export interface GetAppContextResponseOrganizationsItem
{
    id: string;
    name: string;
    logo?: string | null;
}

export interface PostDeviceRequestPairRequest
{
    externalDeviceId: string;
}

export interface PostDeviceRequestPairResponse
{
    code: string;
}

export interface PostDevicePairRequest
{
    code: string;
    deviceName: string;
}

export interface PostDevicePairResponse
{
    success: boolean;
}

export interface GetDeviceResponseItem
{
    id: string;
    name: string;
    status: string | null;
    pairedAt: string | null;
    lastActiveAt: string | null;
}

export interface DeleteDeviceIdResponse
{
    success: boolean;
}

export interface TrsEnterpriseApiClient
{
    get: Get;
    post: Post;
    delete: Delete;
}

export interface Get
{
    health(): Promise<void>;
    media(): Promise<GetMediaResponseItem[]>;
    mediaSignedUrl(uid: string): Promise<GetMediaSignedUrlResponse>;
    auth(param: string): Promise<void>;
    userPermissionsList(userId: string): Promise<void>;
    userPermissionsCheck(userId: string, permission: string): Promise<void>;
    appContext(): Promise<GetAppContextResponse>;
    device(): Promise<GetDeviceResponseItem[]>;
}

export interface Post
{
    mediaUpload(): Promise<PostMediaUploadResponse>;
    register(body: PostRegisterRequest): Promise<void>;
    auth(param: string): Promise<void>;
    userPermissionsSet(body: PostUserPermissionsSetRequest): Promise<void>;
    userPermissionsAdd(body: PostUserPermissionsAddRequest): Promise<void>;
    userPermissionsRemove(body: PostUserPermissionsRemoveRequest): Promise<void>;
    deviceRequestPair(body: PostDeviceRequestPairRequest): Promise<PostDeviceRequestPairResponse>;
    devicePair(body: PostDevicePairRequest): Promise<PostDevicePairResponse>;
}

export interface Delete
{
    mediaId(id: string): Promise<DeleteMediaIdResponse>;
    deviceId(id: string): Promise<DeleteDeviceIdResponse>;
}

export function createClient(options: ClientOptions = {}): TrsEnterpriseApiClient
{
    const baseUrl = options.baseUrl || 'http://localhost:3001';
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
            async health(signal?: AbortSignal): Promise<void> {
                return request<void>('/health', 'get', { signal });
            },

            async media(signal?: AbortSignal): Promise<GetMediaResponseItem[]> {
                return request<GetMediaResponseItem[]>('/api/media/', 'get', { signal });
            },

            async mediaSignedUrl(uid: string, signal?: AbortSignal): Promise<GetMediaSignedUrlResponse> {
                return request<GetMediaSignedUrlResponse>('/api/media/signed-url', 'get', { params: { uid: uid }, signal });
            },

            async auth(param: string, signal?: AbortSignal): Promise<void> {
                return request<void>(`/api/auth/${encodeURIComponent(param)}`, 'get', { signal });
            },

            async userPermissionsList(userId: string, signal?: AbortSignal): Promise<void> {
                return request<void>('/api/user-permissions/list', 'get', { params: { userId: userId }, signal });
            },

            async userPermissionsCheck(userId: string, permission: string, signal?: AbortSignal): Promise<void> {
                return request<void>('/api/user-permissions/check', 'get', { params: { userId: userId, permission: permission }, signal });
            },

            async appContext(signal?: AbortSignal): Promise<GetAppContextResponse> {
                return request<GetAppContextResponse>('/api/app-context/', 'get', { signal });
            },

            async device(signal?: AbortSignal): Promise<GetDeviceResponseItem[]> {
                return request<GetDeviceResponseItem[]>('/api/device/', 'get', { signal });
            },
        },

        post: {
            async mediaUpload(signal?: AbortSignal): Promise<PostMediaUploadResponse> {
                return request<PostMediaUploadResponse>('/api/media/upload', 'post', { signal });
            },

            async register(body: PostRegisterRequest, signal?: AbortSignal): Promise<void> {
                return request<void>('/api/register', 'post', { body, signal });
            },

            async auth(param: string, signal?: AbortSignal): Promise<void> {
                return request<void>(`/api/auth/${encodeURIComponent(param)}`, 'post', { signal });
            },

            async userPermissionsSet(body: PostUserPermissionsSetRequest, signal?: AbortSignal): Promise<void> {
                return request<void>('/api/user-permissions/set', 'post', { body, signal });
            },

            async userPermissionsAdd(body: PostUserPermissionsAddRequest, signal?: AbortSignal): Promise<void> {
                return request<void>('/api/user-permissions/add', 'post', { body, signal });
            },

            async userPermissionsRemove(body: PostUserPermissionsRemoveRequest, signal?: AbortSignal): Promise<void> {
                return request<void>('/api/user-permissions/remove', 'post', { body, signal });
            },

            async deviceRequestPair(body: PostDeviceRequestPairRequest, signal?: AbortSignal): Promise<PostDeviceRequestPairResponse> {
                return request<PostDeviceRequestPairResponse>('/api/device/request-pair', 'post', { body, signal });
            },

            async devicePair(body: PostDevicePairRequest, signal?: AbortSignal): Promise<PostDevicePairResponse> {
                return request<PostDevicePairResponse>('/api/device/pair', 'post', { body, signal });
            },
        },

        delete: {
            async mediaId(id: string, signal?: AbortSignal): Promise<DeleteMediaIdResponse> {
                return request<DeleteMediaIdResponse>(`/api/media/${encodeURIComponent(id)}`, 'delete', { signal });
            },

            async deviceId(id: string, signal?: AbortSignal): Promise<DeleteDeviceIdResponse> {
                return request<DeleteDeviceIdResponse>(`/api/device/${encodeURIComponent(id)}`, 'delete', { signal });
            },
        },
    };
}
