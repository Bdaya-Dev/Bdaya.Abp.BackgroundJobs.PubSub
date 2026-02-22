# [2.0.0](https://github.com/Bdaya-Dev/Bdaya.Abp.BackgroundJobs.PubSub/compare/v1.1.0...v2.0.0) (2026-02-22)


* feat!: migrate BackgroundJobs PubSub to .NET 10 SDK, CPM, xUnit v3, and secure credential APIs ([98bc137](https://github.com/Bdaya-Dev/Bdaya.Abp.BackgroundJobs.PubSub/commit/98bc1375b7579924d024155ec7697be5bf54819f))


### BREAKING CHANGES

* This release migrates test infrastructure to xUnit v3 and updates credential configuration behavior to use explicit GoogleCredential assignment instead of deprecated path-based APIs. Build/CI now require .NET 10 SDK while libraries continue targeting net9.0.

# [1.1.0](https://github.com/Bdaya-Dev/Bdaya.Abp.BackgroundJobs.PubSub/compare/v1.0.0...v1.1.0) (2026-01-26)


### Features

* enhance authentication options in Pub/Sub configuration ([0f702e5](https://github.com/Bdaya-Dev/Bdaya.Abp.BackgroundJobs.PubSub/commit/0f702e5adfb4b160ac09d3cb804b6500afdcaf63))

# 1.0.0 (2026-01-26)


### Bug Fixes

* remove async from methods without await operators ([6ef24ec](https://github.com/Bdaya-Dev/Bdaya.Abp.BackgroundJobs.PubSub/commit/6ef24ec62ec3379955d0ba524aba2c82872f6e54))


### Features

* add semantic-release for automated versioning and publishing ([a69c71c](https://github.com/Bdaya-Dev/Bdaya.Abp.BackgroundJobs.PubSub/commit/a69c71cea93e75b58494782a928440b6bc86ef4b))
