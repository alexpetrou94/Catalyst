//  Generated from Enterprise API
//  Requires TypeScript target ES2015 or higher, or include ES2015 in lib

export interface ProblemDetail<T = Record<string, unknown>> {
    type?: string;
    title?: string;
    status?: number;
    detail?: string;
    instance?: string;
    extensions?: T;
}

export type ApiResult<T> = { data: T; error: null } | { data: null; error: ProblemDetail };

export interface ClientOptions {
    baseUrl?: string;
    credentials?: RequestCredentials;
    headers?: Record<string, string>;
}

export interface RequestOptions {
    params?: Record<string, string | string[] | number | undefined>;
    body?: unknown;
    headers?: Record<string, string>;
    signal?: AbortSignal;
}

export interface GetMediaResponseItem {
    uid: string;
    name: string;
    mimetype: string;
    size: number;
    url: string | null;
    createdAt: string;
}

export interface GetMediaSignedUrlResponse {
    signedUrl: string;
}

export interface PostMediaUploadResponse {
    uid: string;
    name: string;
    mimetype: string;
    size: number;
}

export interface DeleteMediaIdResponse {
    success: boolean;
}

export interface PostRegisterRequest {
    name: string;
    email: string;
    password: string;
    organizationName: string;
    callbackURL?: string;
}

export interface PostUserPermissionsSetRequest {
    userId: string;
    permissions: string[];
}

export interface PostUserPermissionsAddRequest {
    userId: string;
    permissions: string[];
}

export interface PostUserPermissionsRemoveRequest {
    userId: string;
    permissions: string[];
}

export interface GetAppContextResponse {
    hasSession: boolean;
    user: GetAppContextResponseUser | null;
    activeOrganizationId: string | null;
    organizations: GetAppContextResponseOrganizationsItem[];
    permissions: string[];
}

export interface GetAppContextResponseUser {
    id: string;
    name: string;
    email: string;
    avatar?: string | null;
}

export interface GetAppContextResponseOrganizationsItem {
    id: string;
    name: string;
    logo?: string | null;
}

export interface PostDeviceRequestPairRequest {
    externalDeviceId: string;
}

export interface PostDeviceRequestPairResponse {
    code: string;
}

export interface PostDevicePairRequest {
    code: string;
    deviceName: string;
}

export interface PostDevicePairResponse {
    success: boolean;
}

export interface GetDeviceResponseItem {
    id: string;
    name: string;
    status: string | null;
    pairedAt: string | null;
    lastActiveAt: string | null;
}

export interface DeleteDeviceIdResponse {
    success: boolean;
}

export interface EnterpriseApiClient {
    get: Get;
    post: Post;
    delete: Delete;
}

export interface Get {
    health(headers?: Record<string, string>): Promise<ApiResult<void>>;

    media(headers?: Record<string, string>): Promise<ApiResult<GetMediaResponseItem[]>>;

    mediaSignedUrl(uid: string, headers?: Record<string, string>): Promise<ApiResult<GetMediaSignedUrlResponse>>;

    auth(param: string, headers?: Record<string, string>): Promise<ApiResult<void>>;

    userPermissionsList(userId: string, headers?: Record<string, string>): Promise<ApiResult<void>>;

    userPermissionsCheck(userId: string, permission: string, headers?: Record<string, string>): Promise<ApiResult<void>>;

    appContext(headers?: Record<string, string>): Promise<ApiResult<GetAppContextResponse>>;

    device(headers?: Record<string, string>): Promise<ApiResult<GetDeviceResponseItem[]>>;
}

export interface Post {
    mediaUpload(headers?: Record<string, string>): Promise<ApiResult<PostMediaUploadResponse>>;

    register(body: PostRegisterRequest, headers?: Record<string, string>): Promise<ApiResult<void>>;

    auth(param: string, headers?: Record<string, string>): Promise<ApiResult<void>>;

    userPermissionsSet(body: PostUserPermissionsSetRequest, headers?: Record<string, string>): Promise<ApiResult<void>>;

    userPermissionsAdd(body: PostUserPermissionsAddRequest, headers?: Record<string, string>): Promise<ApiResult<void>>;

    userPermissionsRemove(body: PostUserPermissionsRemoveRequest, headers?: Record<string, string>): Promise<ApiResult<void>>;

    deviceRequestPair(body: PostDeviceRequestPairRequest, headers?: Record<string, string>): Promise<ApiResult<PostDeviceRequestPairResponse>>;

    devicePair(body: PostDevicePairRequest, headers?: Record<string, string>): Promise<ApiResult<PostDevicePairResponse>>;
}

export interface Delete {
    mediaId(id: string, headers?: Record<string, string>): Promise<ApiResult<DeleteMediaIdResponse>>;

    deviceId(id: string, headers?: Record<string, string>): Promise<ApiResult<DeleteDeviceIdResponse>>;
}

export function createClient(options: ClientOptions = {}): EnterpriseApiClient {
    const baseUrl = options.baseUrl || 'http://localhost:3001';
    const credentials = options.credentials;
    const headers = options.headers || {};

    async function request<T>(path: string, method: string, init?: RequestOptions): Promise<ApiResult<T>> {
        const base = baseUrl.replace(/\/$/, "");
        const url = new URL(path, base || undefined);
        if (init?.params) {
            const entries = Object.entries(init.params).filter(([, v]) => v !== undefined);
            for (const [key, value] of entries) {
                if (Array.isArray(value)) {
                    for (const v of value) {
                        url.searchParams.append(key, v);
                    }
                } else {
                    url.searchParams.append(key, String(value));
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
            let problemDetail: ProblemDetail = { status: response.status, title: response.statusText };
            try {
                const parsed = JSON.parse(errorText);
                if (parsed && typeof parsed === "object") {
                    const { type, title, status, detail, instance, ...rest } = parsed;
                    problemDetail = {
                        type,
                        title: title || response.statusText,
                        status: status || response.status,
                        detail: detail || errorText,
                        instance,
                        extensions: Object.keys(rest).length > 0 ? rest : undefined,
                    };
                }
            } catch {
                problemDetail = { status: response.status, title: response.statusText, detail: errorText || `HTTP ${response.status}` };
            }
            return { data: null, error: problemDetail };
        }

        const text = await response.text();
        return { data: (text ? JSON.parse(text) : undefined) as T, error: null };
    }

    return {
        get: {
            async health(headers?: Record<string, string>, signal?: AbortSignal): Promise<ApiResult<void>> {
                return request<void>('/health', 'get', { headers, signal });
            },
            async media(headers?: Record<string, string>, signal?: AbortSignal): Promise<ApiResult<GetMediaResponseItem[]>> {
                return request<GetMediaResponseItem[]>('/api/media/', 'get', { headers, signal });
            },
            async mediaSignedUrl(uid: string, headers?: Record<string, string>, signal?: AbortSignal): Promise<ApiResult<GetMediaSignedUrlResponse>> {
                return request<GetMediaSignedUrlResponse>('/api/media/signed-url', 'get', { params: { uid }, headers, signal });
            },
            async auth(param: string, headers?: Record<string, string>, signal?: AbortSignal): Promise<ApiResult<void>> {
                return request<void>(`/api/auth/${encodeURIComponent(param)}`, 'get', { headers, signal });
            },
            async userPermissionsList(userId: string, headers?: Record<string, string>, signal?: AbortSignal): Promise<ApiResult<void>> {
                return request<void>('/api/user-permissions/list', 'get', { params: { userId }, headers, signal });
            },
            async userPermissionsCheck(userId: string, permission: string, headers?: Record<string, string>, signal?: AbortSignal): Promise<ApiResult<void>> {
                return request<void>('/api/user-permissions/check', 'get', { params: { userId, permission }, headers, signal });
            },
            async appContext(headers?: Record<string, string>, signal?: AbortSignal): Promise<ApiResult<GetAppContextResponse>> {
                return request<GetAppContextResponse>('/api/app-context/', 'get', { headers, signal });
            },
            async device(headers?: Record<string, string>, signal?: AbortSignal): Promise<ApiResult<GetDeviceResponseItem[]>> {
                return request<GetDeviceResponseItem[]>('/api/device/', 'get', { headers, signal });
            },
        },
        post: {
            async mediaUpload(headers?: Record<string, string>, signal?: AbortSignal): Promise<ApiResult<PostMediaUploadResponse>> {
                return request<PostMediaUploadResponse>('/api/media/upload', 'post', { headers, signal });
            },
            async register(body: PostRegisterRequest, headers?: Record<string, string>, signal?: AbortSignal): Promise<ApiResult<void>> {
                return request<void>('/api/register', 'post', { body, headers, signal });
            },
            async auth(param: string, headers?: Record<string, string>, signal?: AbortSignal): Promise<ApiResult<void>> {
                return request<void>(`/api/auth/${encodeURIComponent(param)}`, 'post', { headers, signal });
            },
            async userPermissionsSet(body: PostUserPermissionsSetRequest, headers?: Record<string, string>, signal?: AbortSignal): Promise<ApiResult<void>> {
                return request<void>('/api/user-permissions/set', 'post', { body, headers, signal });
            },
            async userPermissionsAdd(body: PostUserPermissionsAddRequest, headers?: Record<string, string>, signal?: AbortSignal): Promise<ApiResult<void>> {
                return request<void>('/api/user-permissions/add', 'post', { body, headers, signal });
            },
            async userPermissionsRemove(body: PostUserPermissionsRemoveRequest, headers?: Record<string, string>, signal?: AbortSignal): Promise<ApiResult<void>> {
                return request<void>('/api/user-permissions/remove', 'post', { body, headers, signal });
            },
            async deviceRequestPair(body: PostDeviceRequestPairRequest, headers?: Record<string, string>, signal?: AbortSignal): Promise<ApiResult<PostDeviceRequestPairResponse>> {
                return request<PostDeviceRequestPairResponse>('/api/device/request-pair', 'post', { body, headers, signal });
            },
            async devicePair(body: PostDevicePairRequest, headers?: Record<string, string>, signal?: AbortSignal): Promise<ApiResult<PostDevicePairResponse>> {
                return request<PostDevicePairResponse>('/api/device/pair', 'post', { body, headers, signal });
            },
        },
        delete: {
            async mediaId(id: string, headers?: Record<string, string>, signal?: AbortSignal): Promise<ApiResult<DeleteMediaIdResponse>> {
                return request<DeleteMediaIdResponse>(`/api/media/${encodeURIComponent(id)}`, 'delete', { headers, signal });
            },
            async deviceId(id: string, headers?: Record<string, string>, signal?: AbortSignal): Promise<ApiResult<DeleteDeviceIdResponse>> {
                return request<DeleteDeviceIdResponse>(`/api/device/${encodeURIComponent(id)}`, 'delete', { headers, signal });
            },
        },
    };
}
