// The text of a value that is a string, number, boolean or bigint, and an empty string for anything else
// (null, undefined, an object, an array). Stringifying such a value would print "[object Object]" or
// "null"; a list cell or a route id that is not a primitive reads as empty instead.
export function primitiveText(value: unknown): string {
  switch (typeof value) {
    case 'string':
      return value
    case 'number':
    case 'boolean':
    case 'bigint':
      return String(value)
    default:
      return ''
  }
}
