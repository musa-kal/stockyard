#!/usr/bin/env bash
# LocalStack ready hook (mounted into /etc/localstack/init/ready.d by docker-compose.yml).
# Creates the events queue with its dead letter queue (5 receives) and the export bucket,
# the same names Terraform uses on AWS.
set -euo pipefail

dlq_url=$(awslocal sqs create-queue --queue-name stockyard-events-dlq --query QueueUrl --output text)
dlq_arn=$(awslocal sqs get-queue-attributes --queue-url "$dlq_url" --attribute-names QueueArn --query Attributes.QueueArn --output text)

attributes=$(mktemp)
cat > "$attributes" <<JSON
{
  "RedrivePolicy": "{\"deadLetterTargetArn\":\"${dlq_arn}\",\"maxReceiveCount\":\"5\"}",
  "VisibilityTimeout": "30"
}
JSON
awslocal sqs create-queue --queue-name stockyard-events --attributes "file://${attributes}" > /dev/null
rm -f "$attributes"

awslocal s3 mb s3://stockyard-exports > /dev/null

echo "stockyard: queues stockyard-events, stockyard-events-dlq and bucket stockyard-exports are ready"
