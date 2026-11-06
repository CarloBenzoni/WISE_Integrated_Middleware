/* global db */
const options = db.adminCommand({ getCmdLineOpts: 1 }).parsed;
const hello = db.adminCommand({ hello: 1 });
print(JSON.stringify({
  dbPath: options.storage?.dbPath ?? null,
  replicaSet: options.replication?.replSetName ?? options.replication?.replSet ?? null,
  ready: hello.isWritablePrimary === true,
}));

