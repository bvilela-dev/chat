export interface UserDto {
  id: string;
  name: string;
  email: string;
  createdAtUtc: string;
}

export interface AuthResponse {
  accessToken: string;
  accessTokenExpiresAtUtc: string;
  refreshToken: string;
  refreshTokenExpiresAtUtc: string;
  user: UserDto;
}

export interface ChatRealtimeMessage {
  messageId: string;
  conversationId: string;
  senderId: string;
  senderName: string;
  content: string;
  createdAtUtc: string;
}

export interface MessageReadDto {
  id: string;
  conversationId: string;
  senderId: string;
  senderName: string;
  content: string;
  createdAtUtc: string;
}

export interface ConversationReadDto {
  id: string;
  lastMessage: string;
  lastMessageAtUtc: string | null;
}