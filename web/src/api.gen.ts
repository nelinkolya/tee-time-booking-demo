/* eslint-disable */
/* tslint:disable */
// @ts-nocheck
/*
 * ---------------------------------------------------------------
 * ## THIS FILE WAS GENERATED VIA SWAGGER-TYPESCRIPT-API        ##
 * ##                                                           ##
 * ## AUTHOR: acacode                                           ##
 * ## SOURCE: https://github.com/acacode/swagger-typescript-api ##
 * ---------------------------------------------------------------
 */

export type BookingStatus = "Confirmed" | "Cancelled";

export interface BookingDto {
  /** @format uuid */
  id: string;
  /** @format int32 */
  teeTimeId: number;
  playerName: string;
  /** @format int32 */
  players: number;
  status: BookingStatus;
  /** @format date-time */
  createdAtUtc: string;
}

export interface BookingRequest {
  playerName: string;
  /** @format int32 */
  players: number;
}

export interface HttpValidationProblemDetails {
  type?: null | string;
  title?: null | string;
  /** @format int32 */
  status?: null | number;
  detail?: null | string;
  instance?: null | string;
  errors?: Record<string, string[]>;
}

export interface JsonWebKey {
  kty: string;
  crv: string;
  x: string;
  y: string;
  kid: string;
  use: string;
  alg: string;
}

export interface JsonWebKeySet {
  keys: JsonWebKey[];
}

export interface ProblemDetails {
  type?: null | string;
  title?: null | string;
  /** @format int32 */
  status?: null | number;
  detail?: null | string;
  instance?: null | string;
}

export interface TeeTimeDto {
  /** @format int32 */
  id: number;
  course: string;
  /** @format date-time */
  startsAt: string;
  /** @format int32 */
  capacity: number;
  /** @format int32 */
  bookedPlayers: number;
  /** @format int32 */
  availableSpots: number;
}

export interface TicketCheckDto {
  /** @format uuid */
  bookingId: string;
  /** @format int32 */
  teeTimeId: number;
  /** @format int32 */
  players: number;
  /** @format date-time */
  startsAt: string;
  /** @format date-time */
  issuedAtUtc: string;
  status: BookingStatus;
}

export interface TicketDto {
  token: string;
}

export interface TicketToVerify {
  token: string;
}

export interface VersionDto {
  version: string;
  commit: null | string;
  /** @format date-time */
  builtAtUtc: null | string;
  environment: string;
}
