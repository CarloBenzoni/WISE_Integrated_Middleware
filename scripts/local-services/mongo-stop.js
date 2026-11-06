/* global db, process */
const options = db.adminCommand({ getCmdLineOpts: 1 }).parsed;
const normalize = (value) => String(value || '').replace(/\//g, '\\').replace(/\\+$/, '').toLowerCase();
if (normalize(options.storage?.dbPath) !== normalize(process.env.WISE_EXPECTED_MONGO_DATA)
  || (options.replication?.replSetName ?? options.replication?.replSet) !== process.env.WISE_EXPECTED_MONGO_REPLICA) {
  throw new Error('MongoDB identity mismatch. Shutdown refused.');
}
// Do not force shutdown or change replica-set configuration.
db.adminCommand({ shutdown: 1, timeoutSecs: 15 });

