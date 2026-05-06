//  Generated from TRS Enterprise API
//  Requires TypeScript target ES2015 or higher, or include ES2015 in lib

export interface ClientOptions
{
    baseUrl?: string;
    credentials?: RequestCredentials;
    headers?: Record<string, string>;
}
export interface Paths
{
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
    user: { id: string; name: string; email: string; avatar?: string | null } | null;
    activeOrganizationId: string | null;
    organizations: { id: string; name: string; logo?: string | null }[];
    permissions: string[];
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
export interface DeleteDeviceIdResponse
{
    success: boolean;
}
export interface TrsEnterpriseApiClient
{
    getHealth(): Promise<void>;
    getMedia(): Promise<{ uid: string; name: string; mimetype: string; size: number; url: string | null; createdAt: string }[]>;
    getMediaSignedUrl(uid: string): Promise<GetMediaSignedUrlResponse>;
    postMediaUpload(): Promise<PostMediaUploadResponse>;
    deleteMediaId(id: string): Promise<DeleteMediaIdResponse>;
    postRegister(body: PostRegisterRequest): Promise<void>;
    getUserPermissionsList(userId: string): Promise<void>;
    getUserPermissionsCheck(userId: string, permission: string): Promise<void>;
    postUserPermissionsSet(body: PostUserPermissionsSetRequest): Promise<void>;
    postUserPermissionsAdd(body: PostUserPermissionsAddRequest): Promise<void>;
    postUserPermissionsRemove(body: PostUserPermissionsRemoveRequest): Promise<void>;
    getAppContext(): Promise<GetAppContextResponse>;
    postDeviceRequestPair(body: PostDeviceRequestPairRequest): Promise<PostDeviceRequestPairResponse>;
    postDevicePair(body: PostDevicePairRequest): Promise<PostDevicePairResponse>;
    getDevice(): Promise<{ id: string; name: string; status: string | null; pairedAt: string | null; lastActiveAt: string | null }[]>;
    deleteDeviceId(id: string): Promise<DeleteDeviceIdResponse>;
}

export function createClient<T extends Paths>(options: ClientOptions = {}): TrsEnterpriseApiClient
{
    const baseUrl = options.baseUrl || 'http://localhost:3001';
    const credentials = options.credentials;
    const headers = options.headers || {};

    async function request(path: string, method: string, init?: any): Promise<any> {
        let url = baseUrl + path;
        if (init?.params) {
            const search = new URLSearchParams(init.params);
            url += '?' + search.toString();
        }
        const response = await fetch(url, {
            method,
            credentials,
            headers: { ...headers, ...init?.headers },
            body: init?.body ? JSON.stringify(init.body) : undefined,
        });
        if (!response.ok) {
            throw new Error(`HTTP ${response.status}`);
        }
        return response.json();
    }

    return {
        async getHealth(): Promise<void> {
            return request('/health', 'get');
        },

        async getMedia(): Promise<{ uid: string; name: string; mimetype: string; size: number; url: string | null; createdAt: string }[]> {
            return request('/api/media/', 'get');
        },

        async getMediaSignedUrl(uid: string): Promise<GetMediaSignedUrlResponse> {
            return request('/api/media/signed-url', 'get', { params: { uid: uid } });
        },

        async postMediaUpload(): Promise<PostMediaUploadResponse> {
            return request('/api/media/upload', 'post');
        },

        async deleteMediaId(id: string): Promise<DeleteMediaIdResponse> {
            return request(`/api/media/${encodeURIComponent(id)}`, 'delete');
        },

        async postRegister(body: PostRegisterRequest): Promise<void> {
            return request('/api/register', 'post', { body });
        },

        async getUserPermissionsList(userId: string): Promise<void> {
            return request('/api/user-permissions/list', 'get', { params: { userId: userId } });
        },

        async getUserPermissionsCheck(userId: string, permission: string): Promise<void> {
            return request('/api/user-permissions/check', 'get', { params: { userId: userId, permission: permission } });
        },

        async postUserPermissionsSet(body: PostUserPermissionsSetRequest): Promise<void> {
            return request('/api/user-permissions/set', 'post', { body });
        },

        async postUserPermissionsAdd(body: PostUserPermissionsAddRequest): Promise<void> {
            return request('/api/user-permissions/add', 'post', { body });
        },

        async postUserPermissionsRemove(body: PostUserPermissionsRemoveRequest): Promise<void> {
            return request('/api/user-permissions/remove', 'post', { body });
        },

        async getAppContext(): Promise<GetAppContextResponse> {
            return request('/api/app-context/', 'get');
        },

        async postDeviceRequestPair(body: PostDeviceRequestPairRequest): Promise<PostDeviceRequestPairResponse> {
            return request('/api/device/request-pair', 'post', { body });
        },

        async postDevicePair(body: PostDevicePairRequest): Promise<PostDevicePairResponse> {
            return request('/api/device/pair', 'post', { body });
        },

        async getDevice(): Promise<{ id: string; name: string; status: string | null; pairedAt: string | null; lastActiveAt: string | null }[]> {
            return request('/api/device/', 'get');
        },

        async deleteDeviceId(id: string): Promise<DeleteDeviceIdResponse> {
            return request(`/api/device/${encodeURIComponent(id)}`, 'delete');
        },
    };
}
