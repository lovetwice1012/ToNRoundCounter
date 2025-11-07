"use strict";
Object.defineProperty(exports, "__esModule", { value: true });
exports.ErrorCode = exports.StreamChannel = exports.RPCMethod = void 0;
exports.isRequestMessage = isRequestMessage;
exports.isResponseMessage = isResponseMessage;
exports.isStreamMessage = isStreamMessage;
exports.generateMessageId = generateMessageId;
exports.createRequest = createRequest;
exports.createResponse = createResponse;
exports.createErrorResponse = createErrorResponse;
exports.createStreamMessage = createStreamMessage;
/**
 * RPC method names (for autocomplete and validation)
 */
var RPCMethod;
(function (RPCMethod) {
    // Auth
    RPCMethod["AUTH_CONNECT"] = "auth.connect";
    // Game management
    RPCMethod["GAME_ROUND_START"] = "game.roundStart";
    RPCMethod["GAME_ROUND_END"] = "game.roundEnd";
    RPCMethod["GAME_PLAYER_UPDATE"] = "game.playerUpdate";
    // Instance management
    RPCMethod["INSTANCE_JOIN"] = "instance.join";
    RPCMethod["INSTANCE_LEAVE"] = "instance.leave";
    RPCMethod["INSTANCE_ALERT"] = "instance.alert";
    // Subscription management
    RPCMethod["SUBSCRIBE"] = "subscribe";
    RPCMethod["UNSUBSCRIBE"] = "unsubscribe";
    // Stats
    RPCMethod["STATS_QUERY"] = "stats.query";
    RPCMethod["STATS_SUBSCRIBE"] = "stats.subscribe";
})(RPCMethod || (exports.RPCMethod = RPCMethod = {}));
/**
 * Stream channel names
 */
var StreamChannel;
(function (StreamChannel) {
    StreamChannel["GAME_PLAYER_UPDATE"] = "game.playerUpdate";
    StreamChannel["INSTANCE_MEMBERS"] = "instance.members";
    StreamChannel["INSTANCE_ALERTS"] = "instance.alerts";
    StreamChannel["STATS_REALTIME"] = "stats.realtime";
})(StreamChannel || (exports.StreamChannel = StreamChannel = {}));
/**
 * Error codes
 */
var ErrorCode;
(function (ErrorCode) {
    ErrorCode["INVALID_PARAMS"] = "INVALID_PARAMS";
    ErrorCode["NOT_FOUND"] = "NOT_FOUND";
    ErrorCode["UNAUTHORIZED"] = "UNAUTHORIZED";
    ErrorCode["FORBIDDEN"] = "FORBIDDEN";
    ErrorCode["CONFLICT"] = "CONFLICT";
    ErrorCode["RATE_LIMIT"] = "RATE_LIMIT";
    ErrorCode["INTERNAL_ERROR"] = "INTERNAL_ERROR";
    ErrorCode["TIMEOUT"] = "TIMEOUT";
})(ErrorCode || (exports.ErrorCode = ErrorCode = {}));
/**
 * Type guard functions
 */
function isRequestMessage(msg) {
    return msg && msg.type === 'request' && msg.method;
}
function isResponseMessage(msg) {
    return msg && msg.type === 'response' && (msg.status === 'success' || msg.status === 'error');
}
function isStreamMessage(msg) {
    return msg && msg.type === 'stream' && msg.event;
}
/**
 * Message ID generator
 */
function generateMessageId() {
    return `msg-${Date.now()}-${Math.random().toString(36).substr(2, 9)}`;
}
/**
 * Create a request message
 */
function createRequest(method, params, id) {
    return {
        version: '1.0',
        id: id || generateMessageId(),
        type: 'request',
        method,
        params,
        timestamp: new Date().toISOString(),
    };
}
/**
 * Create a response message
 */
function createResponse(requestId, result) {
    return {
        version: '1.0',
        id: requestId,
        type: 'response',
        status: 'success',
        result,
        timestamp: new Date().toISOString(),
    };
}
/**
 * Create an error response message
 */
function createErrorResponse(requestId, code, message, details) {
    return {
        version: '1.0',
        id: requestId,
        type: 'response',
        status: 'error',
        error: {
            code,
            message,
            details,
        },
        timestamp: new Date().toISOString(),
    };
}
/**
 * Create a stream message
 */
function createStreamMessage(event, data, id) {
    return {
        version: '1.0',
        id: id || generateMessageId(),
        type: 'stream',
        event,
        data,
        timestamp: new Date().toISOString(),
    };
}
//# sourceMappingURL=index.js.map