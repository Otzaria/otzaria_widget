#!/usr/bin/env python3
"""Read-only dumper for a hive_ce 2.x .hive box (Otzaria app_preferences).

Usage:
  python hive_dump.py [path/to/app_preferences.hive] [--json] [--no-preview] [--full KEY ...]

Default path: %APPDATA%\\otzaria\\app_preferences.hive
The file is copied to a temp dir (the original may be locked / mid-write),
parsed there, and the temp copy is deleted. The original is never modified.
Output: key -> value type -> short preview (strings show only length + first
chars; use --full KEY to print a whole value, --no-preview to hide previews).

Format (hive_ce 2.22, unencrypted box):
  file   = frame*                      (no file header at all)
  frame  = u32le frameLen              (counts itself and the trailing crc)
           key                         (u8 0 + u32le  |  u8 1 + u8 n + n utf8)
           [value]                     (absent  => tombstone / deleted key)
           u32le crc                   (CRC32 zlib over bytes [0, frameLen-4))
  value  = typeId (u8; 21 => followed by u16le real id) + payload
Later frames override earlier ones; a tombstone removes the key.
Parsing stops at the first truncated / CRC-bad frame (Hive truncates the tail).
"""
import datetime
import json
import os
import shutil
import struct
import sys
import tempfile
import zlib

(T_NULL, T_INT, T_DOUBLE, T_BOOL, T_STRING, T_BYTES, T_INTLIST, T_DOUBLELIST,
 T_BOOLLIST, T_STRINGLIST, T_LIST, T_MAP, T_HIVELIST, T_INTSET, T_DOUBLESET,
 T_STRINGSET, T_DATETIME, T_BIGINT, T_DATETIME_TZ, T_SET, T_DURATION,
 T_EXT) = range(22)

TYPE_NAMES = {0: 'null', 1: 'int', 2: 'double', 3: 'bool', 4: 'string',
              5: 'bytes', 6: 'int[]', 7: 'double[]', 8: 'bool[]',
              9: 'string[]', 10: 'list', 11: 'map', 12: 'hivelist',
              13: 'set<int>', 14: 'set<double>', 15: 'set<string>',
              16: 'datetime', 17: 'bigint', 18: 'datetime_tz', 19: 'set',
              20: 'duration'}


class Reader:
    def __init__(self, buf, pos=0, limit=None):
        self.b = buf
        self.p = pos
        self.lim = len(buf) if limit is None else limit

    def need(self, n):
        if n < 0 or self.p + n > self.lim:
            raise EOFError('not enough bytes')

    def u8(self):
        self.need(1)
        v = self.b[self.p]
        self.p += 1
        return v

    def u16(self):
        self.need(2)
        v = struct.unpack_from('<H', self.b, self.p)[0]
        self.p += 2
        return v

    def u32(self):
        self.need(4)
        v = struct.unpack_from('<I', self.b, self.p)[0]
        self.p += 4
        return v

    def f64(self):
        self.need(8)
        v = struct.unpack_from('<d', self.b, self.p)[0]
        self.p += 8
        return v

    def raw(self, n):
        self.need(n)
        v = self.b[self.p:self.p + n]
        self.p += n
        return v

    def hint(self):
        """Hive 'int' is stored as float64 and truncated toward zero."""
        return int(self.f64())

    def string(self, n=None):
        if n is None:
            n = self.u32()
        return self.raw(n).decode('utf-8', errors='replace')

    def key(self):
        t = self.u8()
        if t == 0:
            return self.u32()
        if t == 1:
            return self.string(self.u8())
        raise ValueError('unsupported key type %d' % t)

    def type_id(self):
        t = self.u8()
        return self.u16() if t == T_EXT else t

    def value(self):
        t = self.type_id()
        if t == T_NULL:
            return None
        if t == T_INT:
            return self.hint()
        if t == T_DOUBLE:
            return self.f64()
        if t == T_BOOL:
            return self.u8() > 0
        if t == T_STRING:
            return self.string()
        if t == T_BYTES:
            return bytes(self.raw(self.u32()))
        if t in (T_INTLIST, T_INTSET):
            return [self.hint() for _ in range(self.u32())]
        if t in (T_DOUBLELIST, T_DOUBLESET):
            return [self.f64() for _ in range(self.u32())]
        if t == T_BOOLLIST:
            return [self.u8() > 0 for _ in range(self.u32())]
        if t in (T_STRINGLIST, T_STRINGSET):
            return [self.string() for _ in range(self.u32())]
        if t in (T_LIST, T_SET):
            return [self.value() for _ in range(self.u32())]
        if t == T_MAP:
            m = {}
            for _ in range(self.u32()):
                k = self.value()
                if isinstance(k, (list, dict, bytes)):
                    k = repr(k)
                m[k] = self.value()
            return m
        if t == T_HIVELIST:
            n = self.u32()
            box = self.raw(self.u8()).decode('latin-1')
            return {'hivelist_box': box, 'keys': [self.key() for _ in range(n)]}
        if t == T_DATETIME:  # ms since epoch stored as hive int
            return datetime.datetime.fromtimestamp(
                self.hint() / 1000, datetime.timezone.utc)
        if t == T_DATETIME_TZ:  # float64 ms (+ fractional micros) + isUtc byte
            ms = self.f64()
            utc = self.u8() > 0
            d = datetime.datetime.fromtimestamp(ms / 1000, datetime.timezone.utc)
            return d if utc else d.replace(tzinfo=None)
        if t == T_BIGINT:  # u8 length + decimal string (no u32 prefix)
            return int(self.string(self.u8()))
        if t == T_DURATION:
            return datetime.timedelta(milliseconds=self.f64())
        raise ValueError('unknown typeId %d (custom adapter)' % t)


def parse(buf):
    """Return ({key: (typeName, value)}, info). Last frame wins."""
    pos, state, frames, tombstones, bad = 0, {}, 0, 0, None
    n = len(buf)
    while n - pos >= 4:
        flen = struct.unpack_from('<I', buf, pos)[0]
        if flen < 8 or pos + flen > n:
            bad = ('truncated/invalid frame length %d' % flen, pos)
            break
        crc = struct.unpack_from('<I', buf, pos + flen - 4)[0]
        if (zlib.crc32(buf[pos:pos + flen - 4]) & 0xFFFFFFFF) != crc:
            bad = ('CRC mismatch', pos)
            break
        r = Reader(buf, pos + 4, pos + flen - 4)
        key = r.key()
        if r.p == r.lim:
            state.pop(key, None)
            tombstones += 1
        else:
            start = r.p
            tname = TYPE_NAMES.get(r.type_id(), 'custom')
            r.p = start
            state[key] = (tname, r.value())
        frames += 1
        pos += flen
    if bad is None and pos != n:
        bad = ('%d trailing bytes (partial length prefix)' % (n - pos), pos)
    return state, {'frames': frames, 'tombstones': tombstones, 'bytes': n,
                   'parsed_to': pos, 'error': bad}


SENSITIVE = ('secret', 'token', 'password', 'credential', 'email', 'hash',
             'client-id', 'note', 'path')


def preview(v, width=24, key=''):
    if any(w in str(key).lower() for w in SENSITIVE):
        return '<redacted, %s>' % (('%d chars' % len(v)) if hasattr(v, '__len__') else type(v).__name__)
    if isinstance(v, str):
        return '%d chars: %r' % (len(v), v[:width])
    if isinstance(v, (list, dict, bytes)):
        return 'len=%d' % len(v)
    return repr(v)


def default_path():
    return os.path.join(os.environ.get('APPDATA', ''), 'otzaria',
                        'app_preferences.hive')


def main(argv):
    full = set()
    if '--full' in argv:
        i = argv.index('--full')
        full = set(argv[i + 1:])
        argv = argv[:i]
    args = [a for a in argv if not a.startswith('--')]
    src = args[0] if args else default_path()
    tmp = tempfile.mkdtemp(prefix='hive_dump_')
    try:
        dst = os.path.join(tmp, 'copy.hive')
        try:
            shutil.copy(src, dst)
        except OSError as e:
            print('COPY FAILED (%s): original probably exclusively locked' % e)
            return 2
        with open(dst, 'rb') as f:
            buf = f.read()
        state, info = parse(buf)
        print('# %s: %d bytes, %d frames, %d tombstones, %d live keys, tail=%s'
              % (src, info['bytes'], info['frames'], info['tombstones'],
                 len(state), info['error'] or 'clean (all CRC ok)'))
        if '--json' in argv:
            print(json.dumps({str(k): v[1] for k, v in state.items()},
                             default=str, ensure_ascii=False, indent=1))
        else:
            for k in sorted(state, key=str):
                t, v = state[k]
                if k in full:
                    shown = v
                elif '--no-preview' in argv:
                    shown = ''
                else:
                    shown = preview(v, key=k)
                print('%-46s %-8s %s' % (k, t, shown))
        return 0 if not info['error'] else 1
    finally:
        shutil.rmtree(tmp, ignore_errors=True)


if __name__ == '__main__':
    sys.exit(main(sys.argv[1:]))
