/**
 * 剪贴板消息协议类型定义
 *
 * C# sidecar 通过 stdout 按行输出 JSON，解析后得到以下联合类型之一。
 */

export interface TextMessage {
  type: 'text';
  payload: string;
}

export interface FilesMessage {
  type: 'files';
  payload: string[];
}

export interface ImagePathMessage {
  type: 'image_path';
  payload: string;
}

export interface ErrorMessage {
  type: 'error';
  payload: string;
}

export type ClipboardMessage = TextMessage | FilesMessage | ImagePathMessage | ErrorMessage;
