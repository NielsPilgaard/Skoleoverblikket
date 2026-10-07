#!/usr/bin/env bash
# LocalStack init hook: the pgBackRest repo bucket and the ops bucket.
set -euo pipefail
awslocal s3 mb s3://pgbackrest
awslocal s3 mb s3://skoleoverblikket-ops
