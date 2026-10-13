# Plans against a mocked AWS provider, so these run without credentials:
#   terraform -chdir=infra/terraform test

mock_provider "aws" {
  # The provider validates policy JSON even when mocked, so every generated
  # policy document needs to be a real (empty) policy.
  mock_data "aws_iam_policy_document" {
    defaults = {
      json = "{\"Version\":\"2012-10-17\",\"Statement\":[]}"
    }
  }

  override_data {
    target = data.aws_availability_zones.available
    values = {
      names = ["us-east-1a", "us-east-1b", "us-east-1c"]
    }
  }

  override_data {
    target = data.aws_caller_identity.current
    values = {
      account_id = "123456789012"
    }
  }
}

run "defaults_plan" {
  command = plan

  assert {
    condition     = module.messaging.queue_name == "stockyard-events"
    error_message = "The events queue name is part of the application contract."
  }

  assert {
    condition     = module.messaging.dlq_name == "stockyard-events-dlq"
    error_message = "The dead-letter queue name is part of the application contract."
  }

  assert {
    condition     = module.storage.bucket_name == "stockyard-exports-123456789012"
    error_message = "The export bucket is named after the project and account."
  }
}

run "rejects_uppercase_project" {
  command = plan

  variables {
    project = "Stockyard"
  }

  expect_failures = [var.project]
}

run "rejects_single_zone" {
  command = plan

  variables {
    availability_zone_count = 1
  }

  expect_failures = [var.availability_zone_count]
}

run "rejects_patch_cluster_version" {
  command = plan

  variables {
    cluster_version = "1.34.2"
  }

  expect_failures = [var.cluster_version]
}

run "rejects_desired_above_max" {
  command = plan

  variables {
    node_desired_size = 5
    node_max_size     = 4
  }

  expect_failures = [var.node_desired_size]
}

run "rejects_short_secret_window" {
  command = plan

  variables {
    secret_recovery_window_days = 3
  }

  expect_failures = [var.secret_recovery_window_days]
}
