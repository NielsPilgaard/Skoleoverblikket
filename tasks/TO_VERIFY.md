1. welcome email 
2. download all school data
3. After the first production deploy, check the `CanceledAt` backfill in the prod DB. Expected: `0`.
   ```sql
   select count(*) from "Subscriptions" where "Status" = 3 and "CanceledAt" is null;
   ```
