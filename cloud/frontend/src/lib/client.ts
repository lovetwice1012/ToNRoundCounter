import { createRequest } from '@tonroundcounter/types';

type EventType = 'connected' | 'disconnected' | 'error' | 'message';

export class WebSocketClient {
  private ws: WebSocket | null = null;
  private url: string;
  private listeners: Map<EventType, Set<(...args: any[]) => void>> = new Map();
  private pendingRequests = new Map<string, { resolve: Function; reject: Function; timeout: NodeJS.Timeout }>();
  private reconnectAttempts = 0;
  private maxReconnectAttempts = 5;
  private reconnectDelay = 1000;

  constructor(url: string) {
    this.url = url;
    this.setupListeners();
    this.connect();
  }

  private setupListeners(): void {
    this.listeners.set('connected', new Set());
    this.listeners.set('disconnected', new Set());
    this.listeners.set('error', new Set());
    this.listeners.set('message', new Set());
  }

  on(event: EventType, callback: (...args: any[]) => void): void {
    const set = this.listeners.get(event);
    if (set) {
      set.add(callback);
    }
  }

  off(event: EventType, callback: (...args: any[]) => void): void {
    const set = this.listeners.get(event);
    if (set) {
      set.delete(callback);
    }
  }

  private emit(event: EventType, ...args: any[]): void {
    const set = this.listeners.get(event);
    if (set) {
      set.forEach((callback) => {
        try {
          callback(...args);
        } catch (error) {
          console.error(`Error in ${event} listener:`, error);
        }
      });
    }
  }

  private connect(): void {
    try {
      this.ws = new WebSocket(this.url);

      this.ws.onopen = () => {
        console.log('WebSocket connected');
        this.reconnectAttempts = 0;
        this.emit('connected');
      };

      this.ws.onmessage = (event) => {
        try {
          const message = JSON.parse(event.data);
          this.handleMessage(message);
        } catch (error) {
          console.error('Failed to parse message:', error);
        }
      };

      this.ws.onerror = (event) => {
        console.error('WebSocket error:', event);
        this.emit('error', new Error('WebSocket connection error'));
      };

      this.ws.onclose = () => {
        console.log('WebSocket disconnected');
        this.ws = null;
        this.emit('disconnected');
        this.scheduleReconnect();
      };
    } catch (error) {
      console.error('Failed to create WebSocket:', error);
      this.emit('error', error);
      this.scheduleReconnect();
    }
  }

  private handleMessage(message: any): void {
    if (message.type === 'response') {
      const pending = this.pendingRequests.get(message.id);
      if (pending) {
        clearTimeout(pending.timeout);
        this.pendingRequests.delete(message.id);

        if (message.status === 'success') {
          pending.resolve(message.result);
        } else {
          pending.reject(new Error(message.error?.message || 'Request failed'));
        }
      }
    } else if (message.type === 'stream') {
      this.emit('message', message);
    }
  }

  async call<T = any>(method: string, params?: Record<string, any>): Promise<T> {
    if (!this.ws || this.ws.readyState !== WebSocket.OPEN) {
      throw new Error('WebSocket is not connected');
    }

    const request = createRequest(method, params);

    return new Promise((resolve, reject) => {
      const timeout = setTimeout(() => {
        this.pendingRequests.delete(request.id);
        reject(new Error('Request timeout'));
      }, 30000);

      this.pendingRequests.set(request.id, { resolve, reject, timeout });

      try {
        this.ws!.send(JSON.stringify(request));
      } catch (error) {
        clearTimeout(timeout);
        this.pendingRequests.delete(request.id);
        reject(error);
      }
    });
  }

  private scheduleReconnect(): void {
    if (this.reconnectAttempts >= this.maxReconnectAttempts) {
      console.error('Max reconnection attempts reached');
      this.emit('error', new Error('Failed to reconnect after maximum attempts'));
      return;
    }

    this.reconnectAttempts++;
    const delay = this.reconnectDelay * Math.pow(2, this.reconnectAttempts - 1);
    console.log(`Reconnecting in ${delay}ms (attempt ${this.reconnectAttempts}/${this.maxReconnectAttempts})`);

    setTimeout(() => this.connect(), delay);
  }

  disconnect(): void {
    if (this.ws) {
      this.ws.close();
      this.ws = null;
    }
  }

  isConnected(): boolean {
    return this.ws !== null && this.ws.readyState === WebSocket.OPEN;
  }
}
