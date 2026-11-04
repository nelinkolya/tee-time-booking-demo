// Never imported, only type-checked: tsc fails if a type in api.ts and the server's contract drift apart.
// api.gen.ts is generated from openapi.json (npm run api:types), which a Debug build of the API writes.
import type { AppVersion, Booking, BookingRequest, TeeTime } from "./api";
import type { BookingDto, BookingRequest as BookingRequestDto, TeeTimeDto, VersionDto } from "./api.gen";

type Same<A, B> = [A] extends [B] ? ([B] extends [A] ? true : false) : false;
type Assert<T extends true> = T;

export type Contract = [
  Assert<Same<TeeTime, TeeTimeDto>>,
  Assert<Same<Booking, BookingDto>>,
  Assert<Same<BookingRequest, BookingRequestDto>>,
  Assert<Same<AppVersion, VersionDto>>,
];
