const fs = require("fs");

function patch(target) {
  if (!target) return;

  // Patch realpath
  if (target.promises && target.promises.realpath) {
    const origRealpath = target.promises.realpath;
    target.promises.realpath = async function (p, opts) {
      try {
        return await origRealpath.call(this, p, opts);
      } catch (err) {
        if (err && (err.code === "EPERM" || err.code === "EACCES")) {
          return target.realpathSync(p);
        }
        throw err;
      }
    };
  }

  // Patch promises.mkdir
  if (target.promises && target.promises.mkdir) {
    const origPromMkdir = target.promises.mkdir;
    target.promises.mkdir = async function (p, opt) {
      try {
        return await origPromMkdir.call(this, p, opt);
      } catch (err) {
        if (err && (err.code === "EPERM" || err.code === "EACCES" || err.code === "EEXIST")) {
          if (target.existsSync(p)) return p;
          try {
            return target.mkdirSync(p, opt);
          } catch (e2) {
            if (target.existsSync(p)) return p;
          }
        }
        throw err;
      }
    };
  }

  // Patch cb mkdir
  if (target.mkdir) {
    const origMkdir = target.mkdir;
    target.mkdir = function (p, opt, cb) {
      if (typeof opt === "function") {
        cb = opt;
        opt = {};
      }
      return origMkdir.call(this, p, opt, function (err, ...rest) {
        if (err && (err.code === "EPERM" || err.code === "EACCES" || err.code === "EEXIST")) {
          if (target.existsSync(p)) {
            return cb ? cb(null, p) : undefined;
          }
          try {
            const res = target.mkdirSync(p, opt);
            return cb ? cb(null, res) : undefined;
          } catch (e2) {
            if (target.existsSync(p)) {
              return cb ? cb(null, p) : undefined;
            }
          }
        }
        return cb ? cb(err, ...rest) : undefined;
      });
    };
  }
}

patch(fs);

try {
  const gracefulFs = require("graceful-fs");
  patch(gracefulFs);
} catch (_) {}
