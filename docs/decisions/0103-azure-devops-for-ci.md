# 0103: Azure DevOps Pipelines for build and deploy

Status: accepted

## Context

The repository needs one pipeline that builds and tests the .NET solution (including integration tests that start SQL Server and LocalStack containers), builds and scans four images, pushes them to ECR, and applies Terraform and the Kubernetes manifests with a human approval in front of anything that changes AWS.

The candidates were GitHub Actions and Azure DevOps Pipelines. Both can do every step. Teams running .NET on SQL Server commonly already run Azure DevOps, and showing that setup working against AWS is part of the point of this project.

## Decision

Use Azure DevOps Pipelines with a single `azure-pipelines.yml` and four step templates under `.azure-pipelines/steps/` (dotnet, docker-ecr, terraform, kubectl).

* Build runs on every pull request and every push to main on a Microsoft-hosted `ubuntu-latest` agent, which has Docker for Testcontainers.
* Package and Deploy run only for main. The four images build in parallel matrix jobs; each is scanned with Trivy (as a container, nothing installed on the agent) and the job fails before the push on fixable HIGH or CRITICAL findings.
* Deploy plans first and publishes the plan as an artifact. The apply job is a deployment job against the `stockyard-aws` environment, and the approval check on that environment is the gate.
* AWS access comes from one AWS service connection (AWS Toolkit extension). Non-secret settings come from the `stockyard-aws` variable group. No long-lived keys or application secrets are stored in the pipeline.

## Consequences

* The approval lives in Azure DevOps project settings, not in the YAML. Someone recreating the project has to add the check by hand; the header of `azure-pipelines.yml` lists everything that has to exist first.
* The AWS service connection should use a role with a trust policy rather than static keys where the organisation allows it.
* GitHub Actions would need the same templates rewritten as composite actions or reusable workflows; the shell inside each step would carry over unchanged.
* No pipeline run has been recorded in this repository, so there are no measured durations to quote. The first run's timings belong in docs/platform.md once it has happened.
