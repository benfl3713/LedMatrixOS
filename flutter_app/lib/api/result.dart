/// What went wrong talking to the device.
enum ApiErrorKind { network, timeout, http, parse }

class ApiError implements Exception {
  const ApiError(this.kind, this.message, {this.statusCode, this.errors = const []});

  final ApiErrorKind kind;
  final String message;
  final int? statusCode;

  /// Server validation problems (`400 {errors:[...]}`), one per line. Empty for other errors.
  final List<String> errors;

  @override
  String toString() => message;
}

/// Either a value or a typed [ApiError]. Failures are values, never swallowed.
sealed class Result<T> {
  const Result();

  R when<R>({required R Function(T value) ok, required R Function(ApiError error) err}) => switch (this) {
        Ok<T>(:final value) => ok(value),
        Err<T>(:final error) => err(error),
      };

  T? get valueOrNull => switch (this) {
        Ok<T>(:final value) => value,
        Err<T>() => null,
      };

  ApiError? get errorOrNull => switch (this) {
        Ok<T>() => null,
        Err<T>(:final error) => error,
      };

  bool get isOk => this is Ok<T>;

  /// The value, or throws the [ApiError] (for use inside async providers).
  T getOrThrow() => switch (this) {
        Ok<T>(:final value) => value,
        Err<T>(:final error) => throw error,
      };
}

class Ok<T> extends Result<T> {
  const Ok(this.value);
  final T value;
}

class Err<T> extends Result<T> {
  const Err(this.error);
  final ApiError error;
}
